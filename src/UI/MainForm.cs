using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using EcloudLite.Infrastructure;
using EcloudLite.Models;
using EcloudLite.Protocol;
using EcloudLite.Services;

namespace EcloudLite.UI
{
    internal sealed class MainForm : Form
    {
        private readonly SettingsStore _settingsStore = new SettingsStore();
        private AppSettings _settings;
        private EcloudApiClient _client;
        private LoginService _loginService;
        private DesktopService _desktopService;
        private ConnectionService _connectionService;
        private PathBHandshakeService _pathBHandshakeService;
        private CmssLaunchService _cmssLaunchService;
        private RuntimeSetupService _runtimeSetupService;
        private ConnectResult _lastConnectResult;
        private CmssLaunchResult _cmssSession;

        private TextBox _accountBox;
        private TextBox _passwordBox;
        private TextBox _codeBox;
        private Button _loginButton;
        private Button _sendCodeButton;
        private Button _verifyButton;
        private Button _logoutButton;
        private Button _refreshButton;
        private Button _startButton;
        private Button _shutdownButton;
        private Button _restartButton;
        private Button _uptimeButton;
        private Button _connectButton;
        private Button _handshakeButton;
        private Button _launchButton;
        private Button _disconnectButton;
        private Label _accountLabel;
        private Label _passwordLabel;
        private RadioButton _passwordLoginRadio;
        private RadioButton _smsLoginRadio;
        private CheckBox _rememberPasswordBox;
        private CheckBox _autoLoginBox;
        private CheckBox _saveSessionBox;
        private ComboBox _savedSessionBox;
        private Button _switchSessionButton;
        private Button _deleteSessionButton;
        private Label _statusLabel;
        private Label _backendLabel;
        private DataGridView _desktopGrid;
        private RichTextBox _logBox;

        private AuthChallengeType _challenge = AuthChallengeType.None;
        private string _challengeMobile = string.Empty;
        private string _challengeLoginCode = string.Empty;
        private string _pendingPassword = string.Empty;
        private int _smsCooldown;
        private bool _autoLoginStarted;
        private bool _loadingSessionProfile;
        private readonly System.Windows.Forms.Timer _smsTimer = new System.Windows.Forms.Timer();

        public MainForm()
        {
            Text = AppInfo.ProductName;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 680);
            Size = new Size(1120, 780);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(244, 246, 248);

            BuildUi();
            Logger.EntryWritten += OnLogEntry;
            _smsTimer.Interval = 1000;
            _smsTimer.Tick += SmsTimerTick;
            FormClosed += MainFormClosed;

            _settings = _settingsStore.Load();
            MigrateLegacySession();
            _loadingSessionProfile = true;
            _accountBox.Text = _settings.Username ?? string.Empty;
            _rememberPasswordBox.Checked = _settings.RememberPassword;
            _autoLoginBox.Checked = _settings.AutoLogin;
            _saveSessionBox.Checked = _settings.SaveSession;
            if (string.Equals(_settings.LoginMode, "sms", StringComparison.OrdinalIgnoreCase))
            {
                _smsLoginRadio.Checked = true;
                _rememberPasswordBox.Checked = false;
                _autoLoginBox.Checked = false;
            }
            else
                _passwordLoginRadio.Checked = true;
            RestoreRememberedPassword();
            RefreshSavedSessionList();
            _loadingSessionProfile = false;
            InitializeServices();

            SetStatus(_settings.AutoLogin ? "准备自动登录..." :
                (_settings.SavedSessions.Count > 0 ? "未登录，可从本地会话快速切换" : "未登录"), false);
            SetAuthenticatedControls(false);
            UpdateLoginModeUi();
            Shown += MainFormShown;
            Logger.Info("UI", "main window initialized");
        }

        private void InitializeServices()
        {
            _client = new EcloudApiClient(_settings.DeviceUid);
            _loginService = new LoginService(_client);
            _desktopService = new DesktopService(_client);
            _connectionService = new ConnectionService();
            _pathBHandshakeService = new PathBHandshakeService();
            _runtimeSetupService = new RuntimeSetupService(AppDomain.CurrentDomain.BaseDirectory);
            _cmssLaunchService = new CmssLaunchService(_runtimeSetupService.RuntimeDirectory, _settings.DeviceUid);
        }

        private void MainFormShown(object sender, EventArgs e)
        {
            string reason;
            if (!RuntimeSetupService.IsRuntimeReady(_runtimeSetupService.RuntimeDirectory, out reason))
            {
                Logger.Warn("RUNTIME_SETUP", "runtime missing at startup reason=" + reason);
                DialogResult choice = MessageBox.Show(
                    this,
                    "未检测到云电脑运行组件（" + reason + "）。\r\n\r\n登录、云电脑列表和资源管理仍可使用；启动云电脑前需要从官方安装包提取 runtime。是否现在配置？",
                    "需要配置运行组件",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (choice == DialogResult.Yes) ShowRuntimeSetup();
            }
            else
            {
                Logger.Info("RUNTIME_SETUP", "runtime ready at startup path=" + _runtimeSetupService.RuntimeDirectory);
            }
            BeginAutomaticLogin();
        }

        private void MigrateLegacySession()
        {
            if (_settings.SavedSessions == null) _settings.SavedSessions = new List<SavedSession>();
            bool changed = false;
            if (!string.IsNullOrEmpty(_settings.ProtectedPassword) && string.IsNullOrEmpty(_settings.PasswordUsername))
            {
                _settings.PasswordUsername = _settings.Username ?? string.Empty;
                changed = true;
            }
            if (string.Equals(_settings.LoginMode, "sms", StringComparison.OrdinalIgnoreCase) && _settings.AutoLogin)
            {
                _settings.AutoLogin = false;
                changed = true;
            }
            if (!string.IsNullOrEmpty(_settings.ProtectedToken) && !string.IsNullOrEmpty(_settings.Username))
            {
                string mode = string.Equals(_settings.LoginMode, "sms", StringComparison.OrdinalIgnoreCase) ? "sms" : "password";
                SavedSession session = FindSavedSession(_settings.Username, mode);
                if (session == null)
                {
                    session = new SavedSession
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Username = _settings.Username,
                        LoginMode = mode,
                        ProtectedToken = _settings.ProtectedToken,
                        ProtectedPassword = mode == "password" ? _settings.ProtectedPassword : string.Empty,
                        UpdatedUtc = DateTime.UtcNow.ToString("o")
                    };
                    _settings.SavedSessions.Add(session);
                }
                else
                {
                    session.ProtectedToken = _settings.ProtectedToken;
                    session.UpdatedUtc = DateTime.UtcNow.ToString("o");
                }
                _settings.SelectedSessionId = session.Id;
                _settings.SaveSession = true;
                _settings.ProtectedToken = string.Empty;
                changed = true;
                Logger.Info("SESSION", "legacy saved token migrated; account=" + Logger.MaskAccount(session.Username) + "; mode=" + mode);
            }
            for (int i = 0; i < _settings.SavedSessions.Count; i++)
            {
                SavedSession session = _settings.SavedSessions[i];
                if (string.IsNullOrEmpty(session.Id)) { session.Id = Guid.NewGuid().ToString("N"); changed = true; }
                if (string.IsNullOrEmpty(session.LoginMode)) { session.LoginMode = "password"; changed = true; }
            }
            if (_settings.SaveSession && string.Equals(_settings.LoginMode, "password", StringComparison.OrdinalIgnoreCase) &&
                !_settings.RememberPassword)
            {
                _settings.RememberPassword = true;
                for (int i = 0; i < _settings.SavedSessions.Count; i++)
                {
                    SavedSession session = _settings.SavedSessions[i];
                    if (!string.Equals(session.Id, _settings.SelectedSessionId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(session.ProtectedPassword))
                    {
                        _settings.ProtectedPassword = session.ProtectedPassword;
                        _settings.PasswordUsername = session.Username;
                    }
                    break;
                }
                changed = true;
            }
            if (changed) SavePreferenceSettings("legacy session migration");
        }

        private void RefreshSavedSessionList()
        {
            if (_savedSessionBox == null || _settings == null) return;
            string selectedId = _settings.SelectedSessionId;
            _savedSessionBox.BeginUpdate();
            _savedSessionBox.Items.Clear();
            int selectedIndex = -1;
            for (int i = 0; i < _settings.SavedSessions.Count; i++)
            {
                SavedSession session = _settings.SavedSessions[i];
                _savedSessionBox.Items.Add(session);
                if (string.Equals(session.Id, selectedId, StringComparison.OrdinalIgnoreCase)) selectedIndex = i;
            }
            if (selectedIndex < 0 && _savedSessionBox.Items.Count > 0) selectedIndex = 0;
            _savedSessionBox.SelectedIndex = selectedIndex;
            _savedSessionBox.EndUpdate();
            bool available = _savedSessionBox.Items.Count > 0;
            _savedSessionBox.Enabled = available;
            _switchSessionButton.Enabled = available;
            _deleteSessionButton.Enabled = available && (_client == null || !_client.HasToken);
        }

        private SavedSession SelectedSavedSession()
        {
            return _savedSessionBox == null ? null : _savedSessionBox.SelectedItem as SavedSession;
        }

        private SavedSession FindSavedSession(string username, string loginMode)
        {
            if (_settings == null || _settings.SavedSessions == null) return null;
            for (int i = 0; i < _settings.SavedSessions.Count; i++)
            {
                SavedSession session = _settings.SavedSessions[i];
                if (string.Equals(session.Username, username, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(session.LoginMode, loginMode, StringComparison.OrdinalIgnoreCase)) return session;
            }
            return null;
        }

        private void UpsertSavedSession(string username, string loginMode, string token, string password)
        {
            SavedSession session = FindSavedSession(username, loginMode);
            if (session == null)
            {
                session = new SavedSession { Id = Guid.NewGuid().ToString("N") };
                _settings.SavedSessions.Add(session);
            }
            session.Username = username;
            session.LoginMode = loginMode;
            session.ProtectedToken = _settingsStore.ProtectToken(token);
            if (string.Equals(loginMode, "password", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(password)) session.ProtectedPassword = _settingsStore.ProtectPassword(password);
                else if (string.Equals(_settings.PasswordUsername, username, StringComparison.OrdinalIgnoreCase))
                    session.ProtectedPassword = _settings.ProtectedPassword;
            }
            else session.ProtectedPassword = string.Empty;
            session.UpdatedUtc = DateTime.UtcNow.ToString("o");
            _settings.SelectedSessionId = session.Id;
            Logger.Info("SESSION", "saved session updated; account=" + Logger.MaskAccount(username) + "; mode=" + loginMode +
                "; token_present=" + !string.IsNullOrEmpty(session.ProtectedToken));
        }

        private void RemoveSavedSession(SavedSession session)
        {
            if (session == null) return;
            _settings.SavedSessions.Remove(session);
            if (string.Equals(_settings.SelectedSessionId, session.Id, StringComparison.OrdinalIgnoreCase))
                _settings.SelectedSessionId = string.Empty;
            Logger.Info("SESSION", "saved session deleted; account=" + Logger.MaskAccount(session.Username) + "; mode=" + session.LoginMode);
        }

        private void SwitchSessionClicked(object sender, EventArgs e)
        {
            SavedSession session = SelectedSavedSession();
            if (session == null) return;
            ClearConnectSession();
            _client.ClearToken();
            _desktopGrid.Rows.Clear();
            SetAuthenticatedControls(false);
            TryActivateSavedSession(session);
        }

        private void DeleteSessionClicked(object sender, EventArgs e)
        {
            SavedSession session = SelectedSavedSession();
            if (session == null) return;
            DialogResult answer = MessageBox.Show(this,
                "确认删除本地保存的账号会话？\r\n" + session.Username,
                "删除本地会话", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (answer != DialogResult.OK) return;
            RemoveSavedSession(session);
            if (string.Equals(_settings.Username, session.Username, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_settings.LoginMode, session.LoginMode, StringComparison.OrdinalIgnoreCase))
                _settings.SaveSession = false;
            SavePreferenceSettings("saved session deleted");
            RefreshSavedSessionList();
            SetStatus("本地会话已删除", false);
        }

        private void TryActivateSavedSession(SavedSession session)
        {
            _loadingSessionProfile = true;
            _settings.SelectedSessionId = session.Id;
            _settings.Username = session.Username ?? string.Empty;
            _settings.LoginMode = string.Equals(session.LoginMode, "sms", StringComparison.OrdinalIgnoreCase) ? "sms" : "password";
            _settings.SaveSession = true;
            _accountBox.Text = _settings.Username;
            _saveSessionBox.Checked = true;
            if (_settings.LoginMode == "sms")
            {
                _smsLoginRadio.Checked = true;
                _settings.AutoLogin = false;
                _autoLoginBox.Checked = false;
                _rememberPasswordBox.Checked = false;
                _passwordBox.Clear();
            }
            else
            {
                _passwordLoginRadio.Checked = true;
                _settings.RememberPassword = true;
                _rememberPasswordBox.Checked = true;
                if (!string.IsNullOrEmpty(session.ProtectedPassword))
                {
                    _settings.PasswordUsername = session.Username;
                    _settings.ProtectedPassword = session.ProtectedPassword;
                }
                RestoreRememberedPassword();
            }
            _loadingSessionProfile = false;
            UpdateLoginModeUi();
            SavePreferenceSettings("saved session selected");

            string token = _settingsStore.UnprotectToken(session.ProtectedToken);
            if (string.IsNullOrEmpty(token))
            {
                SetStatus("该会话需要重新登录；登录成功后会自动更新", true);
                Logger.Warn("SESSION", "selected session has no decryptable token; account=" + Logger.MaskAccount(session.Username));
                return;
            }
            _client.SetToken(token);
            SetBusy(true);
            SetStatus("正在切换账号并验证本地会话...", false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    List<Desktop> desktops = _desktopService.GetDesktops();
                    SafeBeginInvoke(delegate
                    {
                        SetBusy(false);
                        SetAuthenticatedControls(true);
                        BindDesktops(desktops);
                        SetStatus("账号切换成功", false);
                    });
                }
                catch (Exception ex)
                {
                    Logger.Exception("SESSION", ex, "saved session activation failed; account=" + Logger.MaskAccount(session.Username));
                    SafeBeginInvoke(delegate
                    {
                        SetBusy(false);
                        _client.ClearToken();
                        SetAuthenticatedControls(false);
                        if (IsSessionExpired(ex)) ShowSessionExpired(session);
                        else SetStatus("会话验证失败（本地会话未删除）：" + Logger.Redact(ex.Message), true);
                    });
                }
            });
        }

        private void ShowSessionExpired(SavedSession session)
        {
            if (session != null)
            {
                session.ProtectedToken = string.Empty;
                session.UpdatedUtc = DateTime.UtcNow.ToString("o");
                _settings.SelectedSessionId = session.Id;
                _settings.SaveSession = true;
            }
            _client.ClearToken();
            SavePreferenceSettings("expired saved session cleared");
            RefreshSavedSessionList();
            SetAuthenticatedControls(false);
            SetStatus("本地会话已过期，请重新登录；成功后会自动更新", true);
            MessageBox.Show(this, "本地会话已过期，请重新登录。\r\n登录成功后将更新保存的会话。",
                "会话已过期", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static bool IsSessionExpired(Exception exception)
        {
            Exception current = exception;
            while (current != null)
            {
                EcloudApiException api = current as EcloudApiException;
                if (api != null)
                {
                    if (string.Equals(api.ErrorCode, "401", StringComparison.OrdinalIgnoreCase)) return true;
                    string message = (api.Message ?? string.Empty).ToLowerInvariant();
                    string[] hints = { "token", "登录失效", "未登录", "请重新登录", "授权", "过期", "expired" };
                    for (int i = 0; i < hints.Length; i++)
                        if (message.Contains(hints[i].ToLowerInvariant())) return true;
                    return false;
                }
                current = current.InnerException;
            }
            return false;
        }

        private void BeginAutomaticLogin()
        {
            if (_autoLoginStarted || _settings == null || !_settings.AutoLogin) return;
            _autoLoginStarted = true;
            SavedSession selectedSession = null;
            for (int i = 0; i < _settings.SavedSessions.Count; i++)
                if (string.Equals(_settings.SavedSessions[i].Id, _settings.SelectedSessionId, StringComparison.OrdinalIgnoreCase))
                    selectedSession = _settings.SavedSessions[i];
            if (selectedSession != null && !string.IsNullOrEmpty(selectedSession.ProtectedToken))
                _client.SetToken(_settingsStore.UnprotectToken(selectedSession.ProtectedToken));
            string protectedPassword = selectedSession != null && !string.IsNullOrEmpty(selectedSession.ProtectedPassword)
                ? selectedSession.ProtectedPassword : _settings.ProtectedPassword;
            string savedPassword = _settings.RememberPassword &&
                string.Equals(_settings.PasswordUsername, _settings.Username, StringComparison.OrdinalIgnoreCase)
                ? _settingsStore.UnprotectPassword(protectedPassword) : string.Empty;
            bool canUsePassword = !string.Equals(_settings.LoginMode, "sms", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(_settings.Username) && !string.IsNullOrEmpty(savedPassword);

            if (!_client.HasToken && !canUsePassword)
            {
                SetAuthenticatedControls(false);
                SetStatus("自动登录缺少可用会话；请手动登录", true);
                Logger.Warn("AUTH", "automatic login skipped; token=false password_fallback=false mode=" + _settings.LoginMode);
                return;
            }

            SetBusy(true);
            SetStatus("正在自动登录...", false);
            Logger.Info("AUTH", "automatic login start token=" + _client.HasToken + " password_fallback=" + canUsePassword);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Desktop> desktops = null;
                LoginResult passwordResult = null;
                Exception finalException = null;
                bool tokenExpired = false;
                if (_client.HasToken)
                {
                    try { desktops = _desktopService.GetDesktops(); }
                    catch (Exception ex)
                    {
                        finalException = ex;
                        tokenExpired = IsSessionExpired(ex);
                        Logger.Exception("AUTH", ex, "saved session validation failed; expired=" + tokenExpired + "; password_fallback=" + canUsePassword);
                        _client.ClearToken();
                    }
                }

                if (desktops == null && canUsePassword && (tokenExpired || finalException == null))
                {
                    try { passwordResult = _loginService.LoginWithPassword(_settings.Username, savedPassword); }
                    catch (Exception ex) { finalException = ex; }
                }

                SafeBeginInvoke(delegate
                {
                    SetBusy(false);
                    if (desktops != null)
                    {
                        SetAuthenticatedControls(true);
                        BindDesktops(desktops);
                        SetStatus("自动登录成功", false);
                        return;
                    }
                    if (passwordResult != null)
                    {
                        if (tokenExpired) ShowSessionExpired(selectedSession);
                        _pendingPassword = savedPassword;
                        HandleLoginResult(passwordResult);
                        return;
                    }
                    if (tokenExpired) ShowSessionExpired(selectedSession);
                    SetAuthenticatedControls(false);
                    SetStatus("自动登录失败" + (finalException == null ? "，请手动登录" : "：" + Logger.Redact(finalException.Message)), true);
                });
            });
        }

        private void LoginModeChanged(object sender, EventArgs e)
        {
            RadioButton radio = sender as RadioButton;
            if (radio == null || !radio.Checked) return;
            _challenge = AuthChallengeType.None;
            _challengeMobile = string.Empty;
            _challengeLoginCode = string.Empty;
            _pendingPassword = string.Empty;
            _codeBox.Clear();
            UpdateLoginModeUi();
            if (_settings == null || _loadingSessionProfile) return;
            _settings.LoginMode = _smsLoginRadio.Checked ? "sms" : "password";
            _loadingSessionProfile = true;
            if (_smsLoginRadio.Checked)
            {
                _settings.AutoLogin = false;
                _rememberPasswordBox.Checked = false;
                _autoLoginBox.Checked = false;
                _passwordBox.Clear();
            }
            else
            {
                _rememberPasswordBox.Checked = _settings.RememberPassword;
                _autoLoginBox.Checked = _settings.AutoLogin;
                RestoreRememberedPassword();
            }
            _loadingSessionProfile = false;
            UpdateLoginModeUi();
            SavePreferenceSettings("login mode changed");
        }

        private void RememberPasswordChanged(object sender, EventArgs e)
        {
            if (_settings == null || _loadingSessionProfile) return;
            if (!_rememberPasswordBox.Checked)
            {
                _autoLoginBox.Checked = false;
                if (_saveSessionBox.Checked && _passwordLoginRadio.Checked) _saveSessionBox.Checked = false;
                _settings.ProtectedPassword = string.Empty;
                _settings.PasswordUsername = string.Empty;
            }
            _settings.RememberPassword = _rememberPasswordBox.Checked;
            SavePreferenceSettings("remember password changed");
        }

        private void AutoLoginChanged(object sender, EventArgs e)
        {
            if (_settings == null || _loadingSessionProfile) return;
            if (_autoLoginBox.Checked && !_rememberPasswordBox.Checked)
                _rememberPasswordBox.Checked = true;
            _settings.AutoLogin = _autoLoginBox.Checked;
            SavePreferenceSettings("auto login changed");
        }

        private void SaveSessionChanged(object sender, EventArgs e)
        {
            if (_settings == null || _loadingSessionProfile) return;
            if (_saveSessionBox.Checked && _passwordLoginRadio.Checked && !_rememberPasswordBox.Checked)
                _rememberPasswordBox.Checked = true;
            _settings.SaveSession = _saveSessionBox.Checked;
            SavePreferenceSettings("save session changed");
        }

        private void SavePreferenceSettings(string reason)
        {
            try { _settingsStore.Save(_settings); }
            catch (Exception ex) { Logger.Exception("CONFIG", ex, reason + " save failed"); }
        }

        private void RestoreRememberedPassword()
        {
            _passwordBox.Text = _settings.RememberPassword &&
                string.Equals(_settings.PasswordUsername, _settings.Username, StringComparison.OrdinalIgnoreCase)
                ? _settingsStore.UnprotectPassword(_settings.ProtectedPassword)
                : string.Empty;
        }

        private void UpdateLoginModeUi()
        {
            bool authenticated = _client != null && _client.HasToken;
            bool smsMode = _smsLoginRadio != null && _smsLoginRadio.Checked;
            bool challengeActive = _challenge != AuthChallengeType.None;
            if (_accountLabel != null) _accountLabel.Text = smsMode ? "账号/手机号" : "账号";
            if (_passwordLabel != null) _passwordLabel.Visible = !smsMode;
            if (_passwordBox != null)
            {
                _passwordBox.Visible = !smsMode;
                _passwordBox.Enabled = !authenticated && !smsMode;
            }
            if (_loginButton != null) _loginButton.Text = smsMode ? "验证码登录" : "登录";
            if (_rememberPasswordBox != null) _rememberPasswordBox.Enabled = !authenticated && !smsMode;
            if (_autoLoginBox != null) _autoLoginBox.Enabled = !authenticated && !smsMode;
            if (_saveSessionBox != null) _saveSessionBox.Enabled = !authenticated;
            if (_accountBox != null) _accountBox.Enabled = !authenticated;
            if (_passwordLoginRadio != null) _passwordLoginRadio.Enabled = !authenticated;
            if (_smsLoginRadio != null) _smsLoginRadio.Enabled = !authenticated;
            if (_codeBox != null) _codeBox.Enabled = !authenticated && (smsMode || challengeActive);
            if (_sendCodeButton != null) _sendCodeButton.Enabled = !authenticated &&
                (smsMode || challengeActive) && _smsCooldown == 0;
            if (_verifyButton != null)
            {
                _verifyButton.Visible = challengeActive;
                _verifyButton.Enabled = !authenticated && challengeActive;
            }
            if (_savedSessionBox != null)
            {
                bool hasSessions = _savedSessionBox.Items.Count > 0;
                _savedSessionBox.Enabled = hasSessions;
                _switchSessionButton.Enabled = hasSessions;
                _deleteSessionButton.Enabled = !authenticated && hasSessions;
            }
        }

        private void BuildUi()
        {
            Panel header = new Panel { Dock = DockStyle.Top, Height = 232, BackColor = Color.White, Padding = new Padding(18, 12, 18, 10) };
            Controls.Add(header);

            Label title = new Label
            {
                Text = "移动云电脑 Lite",
                Font = new Font("Segoe UI", 17F, FontStyle.Bold),
                ForeColor = Color.FromArgb(25, 34, 45),
                AutoSize = true,
                Location = new Point(18, 12)
            };
            header.Controls.Add(title);

            _statusLabel = new Label
            {
                Text = "初始化中",
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(74, 85, 99),
                Location = new Point(250, 20),
                Size = new Size(820, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            header.Controls.Add(_statusLabel);

            Label loginModeLabel = MakeLabel("方式", 18, 54);
            header.Controls.Add(loginModeLabel);
            _passwordLoginRadio = new RadioButton { Text = "密码登录", AutoSize = true, Location = new Point(64, 53), Checked = true };
            _passwordLoginRadio.CheckedChanged += LoginModeChanged;
            header.Controls.Add(_passwordLoginRadio);
            _smsLoginRadio = new RadioButton { Text = "验证码登录", AutoSize = true, Location = new Point(154, 53) };
            _smsLoginRadio.CheckedChanged += LoginModeChanged;
            header.Controls.Add(_smsLoginRadio);
            _rememberPasswordBox = new CheckBox { Text = "记住密码", AutoSize = true, Location = new Point(290, 53) };
            _rememberPasswordBox.CheckedChanged += RememberPasswordChanged;
            header.Controls.Add(_rememberPasswordBox);
            _autoLoginBox = new CheckBox { Text = "自动登录", AutoSize = true, Location = new Point(390, 53) };
            _autoLoginBox.CheckedChanged += AutoLoginChanged;
            header.Controls.Add(_autoLoginBox);
            _saveSessionBox = new CheckBox { Text = "保存会话", AutoSize = true, Location = new Point(490, 53) };
            _saveSessionBox.CheckedChanged += SaveSessionChanged;
            header.Controls.Add(_saveSessionBox);

            _accountLabel = MakeLabel("账号", 18, 91);
            header.Controls.Add(_accountLabel);
            _accountBox = MakeTextBox(112, 87, 210, false);
            header.Controls.Add(_accountBox);

            _passwordLabel = MakeLabel("密码", 340, 91);
            header.Controls.Add(_passwordLabel);
            _passwordBox = MakeTextBox(388, 87, 210, true);
            header.Controls.Add(_passwordBox);

            _loginButton = MakeButton("登录", 616, 86, 94);
            _loginButton.Click += LoginClicked;
            header.Controls.Add(_loginButton);

            _logoutButton = MakeButton("退出登录", 718, 86, 92);
            _logoutButton.Click += LogoutClicked;
            header.Controls.Add(_logoutButton);

            Label codeLabel = MakeLabel("验证码", 18, 137);
            header.Controls.Add(codeLabel);
            _codeBox = MakeTextBox(112, 133, 210, false);
            header.Controls.Add(_codeBox);

            _sendCodeButton = MakeButton("发送验证码", 340, 132, 112);
            _sendCodeButton.Click += SendCodeClicked;
            header.Controls.Add(_sendCodeButton);

            _verifyButton = MakeButton("提交验证", 460, 132, 100);
            _verifyButton.Click += VerifyClicked;
            header.Controls.Add(_verifyButton);

            Button logsButton = MakeButton("日志目录", 568, 132, 92);
            logsButton.Click += delegate
            {
                try { Process.Start("explorer.exe", AppPaths.Logs); }
                catch (Exception ex) { Logger.Exception("UI", ex, "open logs directory failed"); }
            };
            header.Controls.Add(logsButton);

            Label savedSessionLabel = MakeLabel("本地会话", 18, 183);
            header.Controls.Add(savedSessionLabel);
            _savedSessionBox = new ComboBox
            {
                Location = new Point(112, 179),
                Size = new Size(360, 27),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            header.Controls.Add(_savedSessionBox);
            _switchSessionButton = MakeButton("切换账号", 484, 178, 96);
            _switchSessionButton.Click += SwitchSessionClicked;
            header.Controls.Add(_switchSessionButton);
            _deleteSessionButton = MakeButton("删除会话", 588, 178, 96);
            _deleteSessionButton.Click += DeleteSessionClicked;
            header.Controls.Add(_deleteSessionButton);
            Button aboutButton = MakeButton("关于", 696, 178, 84);
            aboutButton.Click += delegate
            {
                Logger.Info("UI", "about window opened");
                using (AboutForm about = new AboutForm()) about.ShowDialog(this);
            };
            header.Controls.Add(aboutButton);
            Button runtimeButton = MakeButton("运行组件", 790, 178, 96);
            runtimeButton.Click += delegate { ShowRuntimeSetup(); };
            header.Controls.Add(runtimeButton);

            SplitContainer mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                BackColor = Color.FromArgb(220, 224, 229)
            };
            Controls.Add(mainSplit);
            mainSplit.BringToFront();
            Shown += delegate
            {
                mainSplit.SplitterDistance = 230;
                mainSplit.Panel1MinSize = 230;
                mainSplit.Panel2MinSize = 160;
                int maximum = mainSplit.Height - mainSplit.Panel2MinSize - mainSplit.SplitterWidth;
                int desired = Math.Min(342, maximum);
                if (desired >= mainSplit.Panel1MinSize)
                    mainSplit.SplitterDistance = desired;
            };

            Panel desktopPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12) };
            mainSplit.Panel1.Controls.Add(desktopPanel);

            Panel toolbar = new Panel { Dock = DockStyle.Top, Height = 110, BackColor = Color.White };
            desktopPanel.Controls.Add(toolbar);
            _refreshButton = MakeButton("刷新", 0, 5, 76);
            _refreshButton.Click += RefreshClicked;
            toolbar.Controls.Add(_refreshButton);
            _startButton = MakeButton("开机", 84, 5, 76);
            _startButton.Click += delegate { RunDesktopOperation("available", false); };
            toolbar.Controls.Add(_startButton);
            _shutdownButton = MakeButton("关机", 168, 5, 76);
            _shutdownButton.Click += delegate { RunDesktopOperation("shutdown", true); };
            toolbar.Controls.Add(_shutdownButton);
            _restartButton = MakeButton("重启", 252, 5, 76);
            _restartButton.Click += delegate { RunDesktopOperation("restart", true); };
            toolbar.Controls.Add(_restartButton);
            _uptimeButton = MakeButton("在线时长", 336, 5, 94);
            _uptimeButton.Click += UptimeClicked;
            toolbar.Controls.Add(_uptimeButton);
            _connectButton = MakeButton("获取连接参数", 0, 43, 120);
            _connectButton.Click += ConnectClicked;
            toolbar.Controls.Add(_connectButton);
            _handshakeButton = MakeButton("建立测试会话", 128, 43, 112);
            _handshakeButton.Click += HandshakeClicked;
            _handshakeButton.Enabled = false;
            toolbar.Controls.Add(_handshakeButton);

            _launchButton = MakeButton("启动云电脑", 248, 43, 112);
            _launchButton.Click += LaunchClicked;
            toolbar.Controls.Add(_launchButton);

            _disconnectButton = MakeButton("断开云电脑", 368, 43, 112);
            _disconnectButton.Click += DisconnectClicked;
            _disconnectButton.Enabled = false;
            toolbar.Controls.Add(_disconnectButton);

            _backendLabel = new Label
            {
                Text = "后端：未识别",
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(62, 73, 86),
                Location = new Point(0, 80),
                Size = new Size(1066, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            toolbar.Controls.Add(_backendLabel);

            _desktopGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                AutoGenerateColumns = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                ColumnHeadersHeight = 34,
                RowTemplate = { Height = 32 }
            };
            _desktopGrid.EnableHeadersVisualStyles = false;
            _desktopGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 241, 244);
            _desktopGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(38, 48, 60);
            _desktopGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 235, 248);
            _desktopGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(25, 34, 45);
            _desktopGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", Width = 170 });
            _desktopGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "状态", Width = 100 });
            _desktopGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Origin", HeaderText = "厂商后端", Width = 130 });
            _desktopGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Instance", HeaderText = "Instance ID", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 210 });
            _desktopGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Machine", HeaderText = "Machine ID", Width = 250 });
            _desktopGrid.SelectionChanged += DesktopSelectionChanged;
            desktopPanel.Controls.Add(_desktopGrid);
            _desktopGrid.BringToFront();

            Panel logPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12, 8, 12, 12) };
            mainSplit.Panel2.Controls.Add(logPanel);
            Label logTitle = new Label { Text = "运行日志", Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.FromArgb(38, 48, 60) };
            logPanel.Controls.Add(logTitle);
            _logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(250, 251, 252),
                ForeColor = Color.FromArgb(45, 55, 67),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9F),
                DetectUrls = false,
                WordWrap = false
            };
            logPanel.Controls.Add(_logBox);
            _logBox.BringToFront();
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            return new Label { Text = text, AutoSize = true, Location = new Point(x, y + 4), ForeColor = Color.FromArgb(55, 65, 77) };
        }

        private static TextBox MakeTextBox(int x, int y, int width, bool password)
        {
            return new TextBox { Location = new Point(x, y), Size = new Size(width, 27), UseSystemPasswordChar = password, BorderStyle = BorderStyle.FixedSingle };
        }

        private static Button MakeButton(string text, int x, int y, int width)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 30),
                FlatStyle = FlatStyle.System,
                UseVisualStyleBackColor = true
            };
        }

        private void LoginClicked(object sender, EventArgs e)
        {
            string account = _accountBox.Text.Trim();
            if (_smsLoginRadio.Checked)
            {
                string code = _codeBox.Text;
                if (string.IsNullOrEmpty(account) || string.IsNullOrWhiteSpace(code))
                {
                    SetStatus("请输入账号/手机号和短信验证码", true);
                    return;
                }
                _pendingPassword = string.Empty;
                _challenge = AuthChallengeType.None;
                SetStatus("正在使用短信验证码登录...", false);
                RunOperation<LoginResult>(
                    "standalone-sms-login",
                    delegate { return _loginService.LoginWithSms(account, code); },
                    HandleLoginResult);
                return;
            }

            string password = _passwordBox.Text;
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
            {
                SetStatus("请输入账号和密码", true);
                return;
            }
            _pendingPassword = password;
            _challenge = AuthChallengeType.None;
            SetStatus("正在登录...", false);
            RunOperation<LoginResult>(
                "password-login",
                delegate { return _loginService.LoginWithPassword(account, password); },
                HandleLoginResult);
        }

        private void HandleLoginResult(LoginResult result)
        {
            if (result.Success)
            {
                CompleteLogin(result.AccessToken);
                return;
            }

            if (result.Challenge == AuthChallengeType.FourA)
            {
                SetStatus("当前账号需要 4A 验证，第一版暂不支持", true);
                return;
            }

            if (result.Challenge != AuthChallengeType.None)
            {
                _challenge = result.Challenge;
                _challengeMobile = string.IsNullOrEmpty(result.Mobile) ? _accountBox.Text.Trim() : result.Mobile;
                _challengeLoginCode = result.LoginCode ?? string.Empty;
                UpdateLoginModeUi();
                SetStatus(string.Format("需要 {0} 验证，手机号 {1}", ChallengeText(_challenge), Logger.Redact(_challengeMobile)), false);
                return;
            }
            SetStatus("登录失败：" + result.Message + (string.IsNullOrEmpty(result.ErrorCode) ? string.Empty : " [" + result.ErrorCode + "]"), true);
        }

        private void SendCodeClicked(object sender, EventArgs e)
        {
            if (_smsCooldown > 0) return;
            string account = _accountBox.Text.Trim();
            bool standalone = _smsLoginRadio.Checked && _challenge == AuthChallengeType.None;
            if (standalone && string.IsNullOrEmpty(account))
            {
                SetStatus("请输入账号或手机号", true);
                return;
            }
            if (!standalone && _challenge == AuthChallengeType.None) return;
            SetStatus("正在请求短信验证码...", false);
            RunOperation<object>(
                "send-sms",
                delegate
                {
                    if (standalone) _loginService.SendStandaloneSmsCode(account);
                    else _loginService.SendChallengeCode(_challenge, _challengeMobile, account);
                    return null;
                },
                delegate
                {
                    _smsCooldown = 60;
                    _smsTimer.Start();
                    _sendCodeButton.Enabled = false;
                    SetStatus("验证码已请求，请查收短信", false);
                });
        }

        private void VerifyClicked(object sender, EventArgs e)
        {
            if (_challenge == AuthChallengeType.None) return;
            SetStatus("正在提交验证码...", false);
            RunOperation<LoginResult>(
                "verify-sms",
                delegate
                {
                    return _loginService.CompleteChallenge(
                        _challenge,
                        _challengeMobile,
                        _accountBox.Text.Trim(),
                        _pendingPassword,
                        _codeBox.Text,
                        _challengeLoginCode);
                },
                HandleLoginResult);
        }

        private void CompleteLogin(string token)
        {
            string passwordToPersist = _pendingPassword;
            bool passwordMode = _passwordLoginRadio.Checked;
            string username = _accountBox.Text.Trim();
            string loginMode = passwordMode ? "password" : "sms";
            _settings.Username = username;
            // Clear secrets before any disk operation so a storage failure cannot leave them visible.
            _passwordBox.Clear();
            _codeBox.Clear();
            _pendingPassword = string.Empty;
            _challenge = AuthChallengeType.None;
            _challengeMobile = string.Empty;
            _challengeLoginCode = string.Empty;

            try
            {
                _settings.ProtectedToken = string.Empty;
                _settings.LoginMode = loginMode;
                _settings.SaveSession = _saveSessionBox.Checked;
                if (passwordMode)
                {
                    _settings.RememberPassword = _rememberPasswordBox.Checked;
                    _settings.AutoLogin = _autoLoginBox.Checked;
                    if (!_settings.RememberPassword)
                    {
                        _settings.ProtectedPassword = string.Empty;
                        _settings.PasswordUsername = string.Empty;
                    }
                    else if (!string.IsNullOrEmpty(passwordToPersist))
                    {
                        _settings.ProtectedPassword = _settingsStore.ProtectPassword(passwordToPersist);
                        _settings.PasswordUsername = username;
                    }
                }
                else
                {
                    _settings.AutoLogin = false;
                }

                if (_settings.SaveSession)
                    UpsertSavedSession(username, loginMode, token, passwordMode ? passwordToPersist : string.Empty);
                else
                {
                    SavedSession existing = FindSavedSession(username, loginMode);
                    if (existing != null) RemoveSavedSession(existing);
                }
                _settingsStore.Save(_settings);
                RefreshSavedSessionList();
            }
            catch (Exception ex)
            {
                Logger.Exception("CONFIG", ex, "login succeeded but session persistence failed");
                _settings.ProtectedToken = string.Empty;
            }

            SetAuthenticatedControls(true);
            SetStatus("登录成功，正在加载云电脑列表...", false);
            RefreshDesktops();
        }

        private void LogoutClicked(object sender, EventArgs e)
        {
            RunOperation<object>(
                "logout",
                delegate { _loginService.Logout(); return null; },
                delegate
                {
                    _settings.ProtectedToken = string.Empty;
                    SavedSession session = FindSavedSession(_settings.Username, _settings.LoginMode);
                    if (session != null)
                    {
                        session.ProtectedToken = string.Empty;
                        session.UpdatedUtc = DateTime.UtcNow.ToString("o");
                        Logger.Info("SESSION", "server logout cleared saved token; account=" + Logger.MaskAccount(session.Username));
                    }
                    _settingsStore.Save(_settings);
                    RefreshSavedSessionList();
                    ClearConnectSession();
                    _desktopGrid.Rows.Clear();
                    SetAuthenticatedControls(false);
                    RestoreRememberedPassword();
                    SetStatus("已退出登录", false);
                });
        }

        private void RefreshClicked(object sender, EventArgs e) { RefreshDesktops(); }

        private void RefreshDesktops()
        {
            ClearConnectSession();
            RunOperation<List<Desktop>>(
                "desktop-list",
                delegate { return _desktopService.GetDesktops(); },
                BindDesktops);
        }

        private void BindDesktops(List<Desktop> desktops)
        {
            _desktopGrid.Rows.Clear();
            int preferredRow = -1;
            for (int i = 0; i < desktops.Count; i++)
            {
                Desktop desktop = desktops[i];
                int index = _desktopGrid.Rows.Add(
                    desktop.MachineName,
                    desktop.ResourceStatus,
                    desktop.OriginCompanyCode,
                    desktop.InstanceId,
                    desktop.MachineId);
                _desktopGrid.Rows[index].Tag = desktop;
                if (!string.IsNullOrEmpty(_settings.LastInstanceId) && desktop.InstanceId == _settings.LastInstanceId)
                    preferredRow = index;
            }
            if (_desktopGrid.Rows.Count > 0)
            {
                _desktopGrid.ClearSelection();
                int row = preferredRow >= 0 ? preferredRow : 0;
                _desktopGrid.Rows[row].Selected = true;
                _desktopGrid.CurrentCell = _desktopGrid.Rows[row].Cells[0];
                UpdateSelectedDesktopMetadata();
            }
            SetStatus("已加载 " + desktops.Count + " 台云电脑", false);
        }

        private void RunDesktopOperation(string operation, bool confirm)
        {
            Desktop desktop = SelectedDesktop();
            if (desktop == null) { SetStatus("请选择一台云电脑", true); return; }
            if (confirm)
            {
                DialogResult choice = MessageBox.Show(
                    this,
                    string.Format("确认对“{0}”执行{1}？", desktop.MachineName, operation == "shutdown" ? "关机" : "重启"),
                    "确认操作",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);
                if (choice != DialogResult.OK) return;
            }
            RunOperation<object>(
                "desktop-operation-" + operation,
                delegate { _desktopService.Operate(desktop, operation); return null; },
                delegate
                {
                    SetStatus("操作已提交：" + operation, false);
                    ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(2500); BeginInvoke(new Action(RefreshDesktops)); });
                });
        }

        private void UptimeClicked(object sender, EventArgs e)
        {
            Desktop desktop = SelectedDesktop();
            if (desktop == null) { SetStatus("请选择一台云电脑", true); return; }
            RunOperation<string>(
                "desktop-uptime",
                delegate { return _desktopService.GetUptime(desktop); },
                delegate(string uptime) { SetStatus("在线时长：" + uptime, false); });
        }

        private void ConnectClicked(object sender, EventArgs e)
        {
            Desktop desktop = SelectedDesktop();
            if (desktop == null) { SetStatus("请选择一台云电脑", true); return; }
            if (!string.Equals(desktop.OriginCompanyCode, "CMSSZTE", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("当前仅支持 CMSSZTE 连接参数获取", true);
                return;
            }
            SetStatus("正在向 CAG 获取短期连接参数...", false);
            RunOperation<ConnectResult>(
                "connect-info",
                delegate { return _connectionService.RequestConnectInfo(desktop); },
                delegate(ConnectResult result)
                {
                    _lastConnectResult = result;
                    _handshakeButton.Enabled = result.Success && result.Parameters != null && result.Parameters.IsComplete;
                    SetStatus(string.Format(
                        "连接参数已获取：长度 {0}，密钥 {1}，IPv6 {2}；可测试握手",
                        result.PlainLength,
                        result.HasKey ? "已包含" : "缺失",
                        result.HasHv6 ? "已包含" : "缺失"),
                        !result.HasKey || !result.HasHv6);
                });
        }

        private void HandshakeClicked(object sender, EventArgs e)
        {
            if (_lastConnectResult == null || _lastConnectResult.Parameters == null)
            {
                SetStatus("请先获取短期连接参数", true);
                return;
            }
            DialogResult choice = MessageBox.Show(
                this,
                "该操作会建立真实云电脑会话，可能中断其他设备上正在使用的连接。是否继续？",
                "建立测试会话",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (choice != DialogResult.OK)
            {
                Logger.Info("PATHB", "test session cancelled by user before network handshake");
                return;
            }
            SetStatus("正在测试 Path B 握手与心跳，最多约 30 秒...", false);
            RunOperation<PathBHandshakeResult>(
                "path-b-handshake",
                delegate { return _pathBHandshakeService.Probe(_lastConnectResult); },
                delegate(PathBHandshakeResult result)
                {
                    SetStatus(string.Format(
                        "握手{0}：TLS {1}，REDQ {2} 字节，心跳 {3} 次（production_claim=false）",
                        result.Success ? "成功" : "未完成",
                        result.TlsVersion,
                        result.RedqBytes,
                        result.HeartCount),
                        !result.Success);
                });
        }

        private void LaunchClicked(object sender, EventArgs e)
        {
            Desktop desktop = SelectedDesktop();
            if (desktop == null) { SetStatus("请选择一台云电脑", true); return; }
            if (!string.Equals(desktop.OriginCompanyCode, "CMSSZTE", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("当前原生渲染器仅支持 CMSSZTE", true);
                return;
            }
            string runtimeReason;
            if (!RuntimeSetupService.IsRuntimeReady(_runtimeSetupService.RuntimeDirectory, out runtimeReason))
            {
                Logger.Warn("RUNTIME_SETUP", "renderer launch blocked; reason=" + runtimeReason);
                SetStatus("运行组件尚未配置：" + runtimeReason, true);
                if (!ShowRuntimeSetup()) return;
            }
            if (_cmssSession != null && _cmssSession.IsRunning)
            {
                SetStatus("已有原生云电脑会话在运行，PID=" + _cmssSession.ProcessId, true);
                return;
            }
            if (_cmssSession != null)
            {
                _cmssSession.Ended -= CmssSessionEnded;
                _cmssSession.Dispose();
                _cmssSession = null;
            }

            DialogResult choice = MessageBox.Show(
                this,
                "将启动官方 CMSS 渲染器并建立真实云电脑会话。该操作可能中断其他设备上的连接，且目前仍标记为 production_claim=false。是否继续？",
                "启动云电脑",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (choice != DialogResult.OK)
            {
                Logger.Info("CMSS", "native renderer launch cancelled by user");
                return;
            }

            SetStatus("正在准备本地控制端口并启动 CMSS 渲染器...", false);
            RunOperation<CmssLaunchResult>(
                "cmss-native-launch",
                delegate { return _cmssLaunchService.Launch(desktop, true); },
                delegate(CmssLaunchResult result)
                {
                    _cmssSession = result;
                    result.Ended += CmssSessionEnded;
                    _launchButton.Enabled = false;
                    _disconnectButton.Enabled = result.IsRunning;
                    SetStatus(string.Format(
                        "原生渲染器已启动：PID {0}，控制端口 {1}（production_claim=false）",
                        result.ProcessId,
                        result.SocketPort),
                        false);
                });
        }

        private bool ShowRuntimeSetup()
        {
            Logger.Info("RUNTIME_SETUP", "runtime setup window opened");
            using (RuntimeSetupForm form = new RuntimeSetupForm(_runtimeSetupService))
                form.ShowDialog(this);
            string reason;
            bool ready = RuntimeSetupService.IsRuntimeReady(_runtimeSetupService.RuntimeDirectory, out reason);
            Logger.Info("RUNTIME_SETUP", "runtime setup window closed ready=" + ready + "; reason=" + reason);
            if (ready) SetStatus("云电脑运行组件已就绪", false);
            return ready;
        }

        private void DisconnectClicked(object sender, EventArgs e)
        {
            CmssLaunchResult session = _cmssSession;
            if (session == null || !session.IsRunning)
            {
                SetStatus("当前没有正在运行的云电脑窗口", true);
                return;
            }
            DialogResult choice = MessageBox.Show(
                this,
                "是否与云电脑断开连接？如长时间未连接使用，云电脑将会被关机，请及时保存正在进行的工作。",
                "断开云电脑",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (choice != DialogResult.OK)
            {
                Logger.Info("CMSS", "native renderer disconnect cancelled by user pid=" + session.ProcessId);
                return;
            }

            Logger.Info("CMSS", "native renderer disconnect confirmed pid=" + session.ProcessId);
            session.Ended -= CmssSessionEnded;
            session.Dispose();
            if (ReferenceEquals(_cmssSession, session)) _cmssSession = null;
            _disconnectButton.Enabled = false;
            _launchButton.Enabled = _client.HasToken;
            SetStatus("已断开云电脑", false);
        }

        private void CmssSessionEnded(object sender, EventArgs e)
        {
            CmssLaunchResult ended = sender as CmssLaunchResult;
            SafeBeginInvoke(delegate
            {
                if (ended != null) ended.Ended -= CmssSessionEnded;
                if (ReferenceEquals(_cmssSession, ended)) _cmssSession = null;
                _disconnectButton.Enabled = false;
                _launchButton.Enabled = _client.HasToken;
                if (!IsDisposed && !Disposing) SetStatus("云电脑窗口已退出", false);
            });
        }

        private Desktop SelectedDesktop()
        {
            if (_desktopGrid.SelectedRows.Count == 0) return null;
            return _desktopGrid.SelectedRows[0].Tag as Desktop;
        }

        private void DesktopSelectionChanged(object sender, EventArgs e)
        {
            ClearConnectSession();
            UpdateSelectedDesktopMetadata();
        }

        private void UpdateSelectedDesktopMetadata()
        {
            Desktop desktop = SelectedDesktop();
            if (desktop == null) return;
            _backendLabel.Text = "后端：" + DesktopService.BackendDescription(desktop.OriginCompanyCode);
            Logger.Info("DESKTOP", "selected desktop instance=" + Logger.ShortId(desktop.InstanceId) + " origin=" + desktop.OriginCompanyCode + " backend=" + DesktopService.BackendDescription(desktop.OriginCompanyCode));
            _settings.LastInstanceId = desktop.InstanceId ?? string.Empty;
            try { _settingsStore.Save(_settings); } catch (Exception ex) { Logger.Exception("CONFIG", ex, "save selected desktop failed"); }
        }

        private void RunOperation<T>(string name, Func<T> work, Action<T> success)
        {
            SetBusy(true);
            Logger.Info("TASK", "operation start name=" + name);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    T result = work();
                    SafeBeginInvoke(delegate
                    {
                        Logger.Info("TASK", "operation success name=" + name);
                        SetBusy(false);
                        success(result);
                    });
                }
                catch (Exception ex)
                {
                    Logger.Exception("TASK", ex, "operation failed name=" + name);
                    SafeBeginInvoke(delegate
                    {
                        SetBusy(false);
                        SavedSession session = FindSavedSession(_settings.Username, _settings.LoginMode);
                        if (_client.HasToken && session != null && IsSessionExpired(ex))
                        {
                            ClearConnectSession();
                            _desktopGrid.Rows.Clear();
                            ShowSessionExpired(session);
                            return;
                        }
                        SetStatus("操作失败：" + Logger.Redact(ex.Message), true);
                    });
                }
            });
        }

        private void SetBusy(bool busy)
        {
            UseWaitCursor = busy;
            _loginButton.Enabled = !busy && !_client.HasToken;
            _logoutButton.Enabled = !busy && _client.HasToken;
            _refreshButton.Enabled = !busy && _client.HasToken;
            _startButton.Enabled = !busy && _client.HasToken;
            _shutdownButton.Enabled = !busy && _client.HasToken;
            _restartButton.Enabled = !busy && _client.HasToken;
            _uptimeButton.Enabled = !busy && _client.HasToken;
            _connectButton.Enabled = !busy && _client.HasToken;
            _handshakeButton.Enabled = !busy && _client.HasToken && _lastConnectResult != null && _lastConnectResult.Parameters != null;
            _launchButton.Enabled = !busy && _client.HasToken && (_cmssSession == null || !_cmssSession.IsRunning);
            _disconnectButton.Enabled = !busy && _cmssSession != null && _cmssSession.IsRunning;
            _verifyButton.Enabled = !busy && !_client.HasToken && _challenge != AuthChallengeType.None;
            _sendCodeButton.Enabled = !busy && !_client.HasToken &&
                (_smsLoginRadio.Checked || _challenge != AuthChallengeType.None) && _smsCooldown == 0;
            _accountBox.Enabled = !busy && !_client.HasToken;
            _passwordLoginRadio.Enabled = !busy && !_client.HasToken;
            _smsLoginRadio.Enabled = !busy && !_client.HasToken;
            _rememberPasswordBox.Enabled = !busy && !_client.HasToken && _passwordLoginRadio.Checked;
            _autoLoginBox.Enabled = !busy && !_client.HasToken && _passwordLoginRadio.Checked;
            _saveSessionBox.Enabled = !busy && !_client.HasToken;
            bool hasSavedSessions = _savedSessionBox.Items.Count > 0;
            _savedSessionBox.Enabled = !busy && hasSavedSessions;
            _switchSessionButton.Enabled = !busy && hasSavedSessions;
            _deleteSessionButton.Enabled = !busy && !_client.HasToken && hasSavedSessions;
            _passwordBox.Enabled = !busy && !_client.HasToken && _passwordLoginRadio.Checked;
            _codeBox.Enabled = !busy && !_client.HasToken && (_smsLoginRadio.Checked || _challenge != AuthChallengeType.None);
        }

        private void SetAuthenticatedControls(bool authenticated)
        {
            _loginButton.Enabled = !authenticated;
            _logoutButton.Enabled = authenticated;
            _refreshButton.Enabled = authenticated;
            _startButton.Enabled = authenticated;
            _shutdownButton.Enabled = authenticated;
            _restartButton.Enabled = authenticated;
            _uptimeButton.Enabled = authenticated;
            _connectButton.Enabled = authenticated;
            _handshakeButton.Enabled = authenticated && _lastConnectResult != null && _lastConnectResult.Parameters != null;
            _launchButton.Enabled = authenticated && (_cmssSession == null || !_cmssSession.IsRunning);
            _disconnectButton.Enabled = authenticated && _cmssSession != null && _cmssSession.IsRunning;
            UpdateLoginModeUi();
        }

        private void SetStatus(string message, bool error)
        {
            _statusLabel.Text = message;
            _statusLabel.ForeColor = error ? Color.FromArgb(174, 47, 47) : Color.FromArgb(55, 83, 103);
            Logger.Info("STATUS", message);
        }

        private void SmsTimerTick(object sender, EventArgs e)
        {
            _smsCooldown--;
            if (_smsCooldown <= 0)
            {
                _smsCooldown = 0;
                _smsTimer.Stop();
                _sendCodeButton.Text = "发送验证码";
                _sendCodeButton.Enabled = !_client.HasToken && (_smsLoginRadio.Checked || _challenge != AuthChallengeType.None);
            }
            else
            {
                _sendCodeButton.Text = "重发 " + _smsCooldown + "s";
            }
        }

        private void OnLogEntry(LogEntry entry)
        {
            SafeBeginInvoke(delegate
            {
                Color color = Color.FromArgb(55, 65, 77);
                if (entry.Level == LogLevel.Warn) color = Color.FromArgb(157, 98, 20);
                if (entry.Level == LogLevel.Error) color = Color.FromArgb(174, 47, 47);
                if (entry.Level == LogLevel.Debug) color = Color.FromArgb(105, 115, 126);
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.SelectionColor = color;
                _logBox.AppendText(entry + Environment.NewLine);
                _logBox.SelectionColor = _logBox.ForeColor;
                _logBox.ScrollToCaret();
            });
        }

        private void SafeBeginInvoke(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(action); } catch { }
        }

        private void MainFormClosed(object sender, FormClosedEventArgs e)
        {
            ClearConnectSession();
            if (_cmssSession != null)
            {
                Logger.Info("UI", "main window closing; cleaning owned CMSS session pid=" + _cmssSession.ProcessId);
                _cmssSession.Ended -= CmssSessionEnded;
                _cmssSession.Dispose();
                _cmssSession = null;
            }
            _smsTimer.Stop();
            Logger.Info("UI", "main window closed");
            Logger.EntryWritten -= OnLogEntry;
        }

        private void ClearConnectSession()
        {
            if (_lastConnectResult != null && _lastConnectResult.Parameters != null)
            {
                _lastConnectResult.Parameters.Key = string.Empty;
                _lastConnectResult.Parameters.Hv6 = string.Empty;
            }
            _lastConnectResult = null;
            if (_handshakeButton != null) _handshakeButton.Enabled = false;
        }

        private static string ChallengeText(AuthChallengeType challenge)
        {
            if (challenge == AuthChallengeType.DeviceTrust) return "设备信任";
            if (challenge == AuthChallengeType.TwoFactor) return "二次";
            if (challenge == AuthChallengeType.EnhancedSms) return "增强短信";
            return challenge.ToString();
        }
    }
}
