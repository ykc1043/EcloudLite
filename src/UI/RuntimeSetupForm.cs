using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using EcloudLite.Infrastructure;
using EcloudLite.Services;

namespace EcloudLite.UI
{
    internal sealed class RuntimeSetupForm : Form
    {
        private readonly RuntimeSetupService _service;
        private readonly Label _runtimeStatus;
        private readonly TextBox _installerBox;
        private readonly Label _sevenZipStatus;
        private readonly Button _browseButton;
        private readonly Button _detectButton;
        private readonly Button _downloadButton;
        private readonly Button _installButton;
        private readonly Button _buildButton;
        private readonly Button _closeButton;
        private readonly ProgressBar _progress;
        private readonly RichTextBox _logBox;
        private string _sevenZipPath;
        private bool _working;

        public RuntimeSetupForm(RuntimeSetupService service)
        {
            if (service == null) throw new ArgumentNullException("service");
            _service = service;

            Text = "配置云电脑运行组件";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 600);
            Size = new Size(820, 680);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.White;

            Label title = new Label
            {
                Text = "配置官方运行组件",
                Font = new Font("Segoe UI", 17F, FontStyle.Bold),
                ForeColor = Color.FromArgb(25, 34, 45),
                AutoSize = true,
                Location = new Point(22, 18)
            };
            Controls.Add(title);

            Label description = new Label
            {
                Text = "Lite 不分发官方 runtime。请从官方页面下载 Windows 安装包，随后由本工具在本机提取所需组件。",
                ForeColor = Color.FromArgb(74, 85, 99),
                Location = new Point(24, 57),
                Size = new Size(750, 42),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(description);

            _runtimeStatus = MakeStatusLabel(24, 101, 750);
            Controls.Add(_runtimeStatus);

            Button officialButton = MakeButton("打开官方下载页", 24, 137, 132);
            officialButton.Click += OpenOfficialPage;
            Controls.Add(officialButton);

            Label packageLabel = new Label
            {
                Text = "官方安装包",
                AutoSize = true,
                ForeColor = Color.FromArgb(55, 65, 77),
                Location = new Point(24, 190)
            };
            Controls.Add(packageLabel);

            _installerBox = new TextBox
            {
                Location = new Point(114, 185),
                Size = new Size(568, 27),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_installerBox);

            _browseButton = MakeButton("选择...", 692, 184, 86);
            _browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _browseButton.Click += BrowseInstaller;
            Controls.Add(_browseButton);

            _sevenZipStatus = MakeStatusLabel(24, 229, 750);
            Controls.Add(_sevenZipStatus);

            _detectButton = MakeButton("重新检测", 24, 263, 96);
            _detectButton.Click += delegate { RefreshState(true); };
            Controls.Add(_detectButton);

            _downloadButton = MakeButton("下载 7-Zip", 130, 263, 106);
            _downloadButton.Click += DownloadSevenZip;
            Controls.Add(_downloadButton);

            _installButton = MakeButton("安装 7-Zip", 246, 263, 106);
            _installButton.Click += InstallSevenZip;
            Controls.Add(_installButton);

            _buildButton = MakeButton("开始提取并配置", 24, 310, 150);
            _buildButton.Click += BuildRuntime;
            Controls.Add(_buildButton);

            _progress = new ProgressBar
            {
                Location = new Point(186, 313),
                Size = new Size(592, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(_progress);

            Label logLabel = new Label
            {
                Text = "详细日志",
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(38, 48, 60),
                Location = new Point(24, 356)
            };
            Controls.Add(logLabel);

            _logBox = new RichTextBox
            {
                Location = new Point(24, 380),
                Size = new Size(754, 210),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true,
                WordWrap = false,
                DetectUrls = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(250, 251, 252),
                ForeColor = Color.FromArgb(45, 55, 67),
                Font = new Font("Consolas", 9F)
            };
            Controls.Add(_logBox);

            _closeButton = MakeButton("关闭", 690, 603, 88);
            _closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _closeButton.Click += delegate { Close(); };
            Controls.Add(_closeButton);
            CancelButton = _closeButton;

            Shown += delegate
            {
                AppendLog("配置向导已打开；数据目录：" + AppPaths.Root);
                RefreshState(false);
            };
            FormClosing += RuntimeSetupFormClosing;
        }

        public bool RuntimeReady
        {
            get
            {
                string reason;
                return RuntimeSetupService.IsRuntimeReady(_service.RuntimeDirectory, out reason);
            }
        }

        private static Label MakeStatusLabel(int x, int y, int width)
        {
            return new Label
            {
                Location = new Point(x, y),
                Size = new Size(width, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(55, 83, 103)
            };
        }

        private static Button MakeButton(string text, int x, int y, int width)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 30),
                UseVisualStyleBackColor = true
            };
        }

        private void RefreshState(bool log)
        {
            string reason;
            bool ready = RuntimeSetupService.IsRuntimeReady(_service.RuntimeDirectory, out reason);
            _runtimeStatus.Text = "Runtime：" + (ready ? "已就绪" : "未就绪（" + reason + "）");
            _runtimeStatus.ForeColor = ready ? Color.FromArgb(38, 112, 72) : Color.FromArgb(174, 47, 47);

            _sevenZipPath = RuntimeSetupService.FindSevenZip();
            bool sevenZipReady = !string.IsNullOrEmpty(_sevenZipPath);
            _sevenZipStatus.Text = sevenZipReady ? "7-Zip：已检测到 " + _sevenZipPath : "7-Zip：未检测到，请下载并安装后重新检测";
            _sevenZipStatus.ForeColor = sevenZipReady ? Color.FromArgb(38, 112, 72) : Color.FromArgb(157, 98, 20);

            string downloaded = RuntimeSetupService.SevenZipInstallerPath();
            _installButton.Enabled = !_working && File.Exists(downloaded);
            _downloadButton.Enabled = !_working;
            _buildButton.Enabled = !_working && !ready && sevenZipReady;
            if (log) AppendLog(ready ? "Runtime 检测通过。" : "Runtime 检测未通过：" + reason);
            if (log) AppendLog(sevenZipReady ? "已检测到 7-Zip：" + _sevenZipPath : "未检测到 7-Zip。 ");
        }

        private void OpenOfficialPage(object sender, EventArgs e)
        {
            try
            {
                Logger.Info("RUNTIME_SETUP", "opening official download page");
                Process.Start(new ProcessStartInfo { FileName = RuntimeSetupService.OfficialDownloadPage, UseShellExecute = true });
                AppendLog("已打开移动云电脑官方下载页，请选择 Windows 版本安装包。 ");
            }
            catch (Exception exception)
            {
                Logger.Exception("RUNTIME_SETUP", exception, "open official download page failed");
                ShowError("无法打开官方下载页：" + exception.Message);
            }
        }

        private void BrowseInstaller(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择移动云电脑 Windows 安装包";
                dialog.Filter = "Windows 安装程序 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _installerBox.Text = dialog.FileName;
                AppendLog("已选择官方安装包：" + dialog.FileName);
            }
        }

        private void DownloadSevenZip(object sender, EventArgs e)
        {
            string destination;
            try { destination = RuntimeSetupService.SevenZipInstallerPath(); }
            catch (Exception exception) { ShowError("无法创建下载目录：" + exception.Message); return; }

            DialogResult choice = MessageBox.Show(
                this,
                "将从 7-Zip 官方网站下载 " + RuntimeSetupService.SevenZipDownloadName + " 到：\r\n" + destination + "\r\n\r\n是否继续？",
                "下载 7-Zip",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (choice != DialogResult.OK) return;

            SetWorking(true, false);
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 0;
            AppendLog("开始从 7-Zip 官网下载：" + RuntimeSetupService.SevenZipDownloadUrl);
            Logger.Info("RUNTIME_SETUP", "7zip download start destination=" + destination);
            WebClient client = new WebClient();
            client.DownloadProgressChanged += delegate(object downloadSender, DownloadProgressChangedEventArgs args)
            {
                int percent = Math.Max(0, Math.Min(100, args.ProgressPercentage));
                _progress.Value = percent;
            };
            client.DownloadFileCompleted += delegate(object downloadSender, System.ComponentModel.AsyncCompletedEventArgs args)
            {
                client.Dispose();
                SetWorking(false, false);
                if (args.Error != null)
                {
                    TryDeleteDownload(destination);
                    Logger.Exception("RUNTIME_SETUP", args.Error, "7zip download failed");
                    AppendLog("7-Zip 下载失败：" + Logger.Redact(args.Error.Message));
                    RefreshState(false);
                    ShowError("7-Zip 下载失败，详情已写入日志。 ");
                    return;
                }
                if (args.Cancelled)
                {
                    TryDeleteDownload(destination);
                    AppendLog("7-Zip 下载已取消。 ");
                    RefreshState(false);
                    return;
                }
                if (!File.Exists(destination) || new FileInfo(destination).Length < 500000)
                {
                    TryDeleteDownload(destination);
                    Logger.Error("RUNTIME_SETUP", "7zip download was unexpectedly small");
                    AppendLog("7-Zip 下载结果无效，已删除残缺文件。 ");
                    RefreshState(false);
                    ShowError("下载结果不像有效的 7-Zip 安装包，请重试。 ");
                    return;
                }
                Logger.Info("RUNTIME_SETUP", "7zip download complete destination=" + destination + "; bytes=" + new FileInfo(destination).Length);
                AppendLog("7-Zip 安装包下载完成：" + destination);
                RefreshState(false);
                DialogResult install = MessageBox.Show(this, "7-Zip 已下载。是否现在启动安装程序？", "下载完成", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (install == DialogResult.Yes) StartSevenZipInstaller(destination);
            };
            try { client.DownloadFileAsync(new Uri(RuntimeSetupService.SevenZipDownloadUrl), destination); }
            catch (Exception exception)
            {
                client.Dispose();
                SetWorking(false, false);
                Logger.Exception("RUNTIME_SETUP", exception, "7zip download could not start");
                RefreshState(false);
                ShowError("无法开始下载 7-Zip：" + exception.Message);
            }
        }

        private void InstallSevenZip(object sender, EventArgs e)
        {
            string installer = RuntimeSetupService.SevenZipInstallerPath();
            if (!File.Exists(installer))
            {
                ShowError("尚未找到已下载的 7-Zip 安装包。 ");
                return;
            }
            StartSevenZipInstaller(installer);
        }

        private void StartSevenZipInstaller(string installer)
        {
            try
            {
                Logger.Info("RUNTIME_SETUP", "user confirmed 7zip installer launch path=" + installer);
                Process.Start(new ProcessStartInfo { FileName = installer, UseShellExecute = true });
                AppendLog("已启动 7-Zip 安装程序。安装完成后请点击“重新检测”。 ");
            }
            catch (Exception exception)
            {
                Logger.Exception("RUNTIME_SETUP", exception, "launch 7zip installer failed");
                ShowError("无法启动 7-Zip 安装程序：" + exception.Message);
            }
        }

        private void BuildRuntime(object sender, EventArgs e)
        {
            string installer = _installerBox.Text.Trim();
            if (string.IsNullOrEmpty(installer) || !File.Exists(installer))
            {
                ShowError("请先选择从移动云电脑官网下载的 Windows 安装包。 ");
                return;
            }
            RefreshState(false);
            if (string.IsNullOrEmpty(_sevenZipPath))
            {
                ShowError("未检测到 7-Zip，请先下载或安装后重新检测。 ");
                return;
            }

            DialogResult choice = MessageBox.Show(
                this,
                "提取过程临时需要约 2 GiB 可用空间，完成后仅保留裁剪后的 runtime。该过程不会安装官方客户端。是否继续？",
                "开始提取",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (choice != DialogResult.OK) return;

            SetWorking(true, true);
            AppendLog("开始提取和组装 runtime，请勿关闭窗口。 ");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    RuntimeSetupResult result = _service.Build(installer, _sevenZipPath, AppendLogThreadSafe);
                    SafeBeginInvoke(delegate
                    {
                        SetWorking(false, false);
                        RefreshState(false);
                        AppendLog(string.Format("配置成功：{0} 个文件，{1:F2} MiB。", result.FileCount, result.Bytes / 1048576.0));
                        MessageBox.Show(this, "运行组件配置完成，现在可以启动云电脑。", "配置完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
                }
                catch (Exception exception)
                {
                    SafeBeginInvoke(delegate
                    {
                        SetWorking(false, false);
                        RefreshState(false);
                        ShowError("运行组件配置失败：" + Logger.Redact(exception.Message) + "\r\n\r\n详细信息和保留的临时目录已写入日志。 ");
                    });
                }
            });
        }

        private void SetWorking(bool working, bool marquee)
        {
            _working = working;
            UseWaitCursor = working;
            _installerBox.Enabled = !working;
            _browseButton.Enabled = !working;
            _detectButton.Enabled = !working;
            _downloadButton.Enabled = !working;
            _installButton.Enabled = !working && File.Exists(RuntimeSetupService.SevenZipInstallerPath());
            _buildButton.Enabled = !working;
            _closeButton.Enabled = !working;
            _progress.Style = marquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (!working && _progress.Style == ProgressBarStyle.Continuous) _progress.Value = 0;
        }

        private void AppendLogThreadSafe(string message)
        {
            SafeBeginInvoke(delegate { AppendLog(message); });
        }

        private void AppendLog(string message)
        {
            if (_logBox.IsDisposed) return;
            _logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + Logger.Redact(message) + Environment.NewLine);
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }

        private void SafeBeginInvoke(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(action); } catch { }
        }

        private void RuntimeSetupFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_working) return;
            e.Cancel = true;
            MessageBox.Show(this, "下载或提取仍在进行，请等待操作完成。", "正在配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void TryDeleteDownload(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception exception) { Logger.Exception("RUNTIME_SETUP", exception, "delete incomplete 7zip download failed path=" + path); }
        }

        private void ShowError(string message)
        {
            MessageBox.Show(this, message, "运行组件配置", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
