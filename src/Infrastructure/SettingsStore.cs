using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace EcloudLite.Infrastructure
{
    internal sealed class SavedSession
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string LoginMode { get; set; }
        public string ProtectedToken { get; set; }
        public string ProtectedPassword { get; set; }
        public string UpdatedUtc { get; set; }

        public override string ToString()
        {
            string account = string.IsNullOrEmpty(Username) ? "未命名账号" : Username;
            string mode = string.Equals(LoginMode, "sms", StringComparison.OrdinalIgnoreCase) ? "验证码" : "密码";
            string state = string.IsNullOrEmpty(ProtectedToken) ? "需重新登录" : "可用会话";
            return account + " · " + mode + " · " + state;
        }
    }

    internal sealed class AppSettings
    {
        public string Username { get; set; }
        public string DeviceUid { get; set; }
        public string ProtectedToken { get; set; }
        public string ProtectedPassword { get; set; }
        public bool RememberPassword { get; set; }
        public bool AutoLogin { get; set; }
        public string LoginMode { get; set; }
        public string PasswordUsername { get; set; }
        public bool SaveSession { get; set; }
        public string SelectedSessionId { get; set; }
        public List<SavedSession> SavedSessions { get; set; }
        public string LastInstanceId { get; set; }
    }

    internal sealed class SettingsStore
    {
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public AppSettings Load()
        {
            AppPaths.EnsureCreated();
            if (!File.Exists(AppPaths.Settings))
            {
                Logger.Info("CONFIG", "settings file not found; using defaults");
                return NewSettings();
            }

            try
            {
                string text = File.ReadAllText(AppPaths.Settings, Encoding.UTF8);
                AppSettings settings = _json.Deserialize<AppSettings>(text) ?? NewSettings();
                if (string.IsNullOrEmpty(settings.DeviceUid)) settings.DeviceUid = Guid.NewGuid().ToString();
                if (settings.SavedSessions == null) settings.SavedSessions = new List<SavedSession>();
                Logger.Info("CONFIG", "settings loaded; username=" + Logger.MaskAccount(settings.Username) + "; device=" + Logger.ShortId(settings.DeviceUid));
                return settings;
            }
            catch (Exception ex)
            {
                Logger.Exception("CONFIG", ex, "settings load failed; using defaults");
                return NewSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            AppPaths.EnsureCreated();
            string temp = AppPaths.Settings + ".tmp";
            string json = _json.Serialize(settings);
            File.WriteAllText(temp, json, new UTF8Encoding(false));

            if (File.Exists(AppPaths.Settings))
            {
                string backup = AppPaths.Settings + ".bak";
                File.Replace(temp, AppPaths.Settings, backup, true);
                try { File.Delete(backup); } catch { }
            }
            else
            {
                File.Move(temp, AppPaths.Settings);
            }
            Logger.Info("CONFIG", "settings saved; token_present=" + (!string.IsNullOrEmpty(settings.ProtectedToken)) +
                "; password_present=" + (!string.IsNullOrEmpty(settings.ProtectedPassword)) +
                "; remember=" + settings.RememberPassword + "; auto_login=" + settings.AutoLogin +
                "; mode=" + (string.IsNullOrEmpty(settings.LoginMode) ? "password" : settings.LoginMode) +
                "; saved_sessions=" + (settings.SavedSessions == null ? 0 : settings.SavedSessions.Count) +
                "; save_session=" + settings.SaveSession);
        }

        public string ProtectToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return string.Empty;
            try
            {
                byte[] plain = Encoding.UTF8.GetBytes(token);
                byte[] protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(protectedBytes);
            }
            catch (Exception ex)
            {
                Logger.Exception("CONFIG", ex, "token protection unavailable; token will not be persisted");
                return string.Empty;
            }
        }

        public string UnprotectToken(string protectedToken)
        {
            if (string.IsNullOrEmpty(protectedToken)) return string.Empty;
            try
            {
                byte[] protectedBytes = Convert.FromBase64String(protectedToken);
                byte[] plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex)
            {
                Logger.Exception("CONFIG", ex, "saved token decrypt failed");
                return string.Empty;
            }
        }

        public string ProtectPassword(string password)
        {
            return ProtectSecret(password, "password");
        }

        public string UnprotectPassword(string protectedPassword)
        {
            return UnprotectSecret(protectedPassword, "password");
        }

        private string ProtectSecret(string value, string label)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            try
            {
                byte[] plain = Encoding.UTF8.GetBytes(value);
                byte[] protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(protectedBytes);
            }
            catch (Exception ex)
            {
                Logger.Exception("CONFIG", ex, label + " protection unavailable; secret will not be persisted");
                return string.Empty;
            }
        }

        private string UnprotectSecret(string protectedValue, string label)
        {
            if (string.IsNullOrEmpty(protectedValue)) return string.Empty;
            try
            {
                byte[] protectedBytes = Convert.FromBase64String(protectedValue);
                byte[] plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex)
            {
                Logger.Exception("CONFIG", ex, "saved " + label + " decrypt failed");
                return string.Empty;
            }
        }

        private static AppSettings NewSettings()
        {
            return new AppSettings
            {
                Username = string.Empty,
                DeviceUid = Guid.NewGuid().ToString(),
                ProtectedToken = string.Empty,
                ProtectedPassword = string.Empty,
                RememberPassword = false,
                AutoLogin = false,
                LoginMode = "password",
                PasswordUsername = string.Empty,
                SaveSession = false,
                SelectedSessionId = string.Empty,
                SavedSessions = new List<SavedSession>(),
                LastInstanceId = string.Empty
            };
        }
    }
}
