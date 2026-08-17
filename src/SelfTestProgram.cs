using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using EcloudLite.Infrastructure;
using EcloudLite.Protocol;
using EcloudLite.Services;
using EcloudLite.UI;

namespace EcloudLite
{
    internal static class SelfTestProgram
    {
        private static int _failures;

        private static int Main()
        {
            try
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ECLOUDLITE_DATA_DIR")))
                    Environment.SetEnvironmentVariable("ECLOUDLITE_DATA_DIR", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-data"));
                Logger.Initialize();
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.WriteLine("EcloudLite offline self-test");
                TestCryptoRoundTrip();
                TestSignedUrl();
                TestRedaction();
                TestEncryptedResponseHandling();
                TestLoginPayloads();
                TestCsapCrypto();
                TestPathBProtocol();
                TestKeepAliveDefaults();
                TestCmssLaunchCrypto();
                TestMissingCmssRuntimeMessage();
                TestRuntimeSetupSupport();
                TestCmssControlServer();
                TestCmssSessionLifecycle();
                TestProtectedSettings();
                TestSessionExpiryClassification();
                TestAppInfo();
                TestFileRedaction();
                Console.WriteLine(_failures == 0 ? "PASS" : "FAILURES=" + _failures);
                return _failures == 0 ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("SELFTEST_FATAL_TYPE=" + exception.GetType().FullName);
                Console.Error.WriteLine("SELFTEST_FATAL_MESSAGE=" + (exception.Message ?? string.Empty));
                Console.Error.WriteLine("SELFTEST_FATAL_STACK=" + (exception.StackTrace ?? "<no stack>"));
                return 2;
            }
        }

        private static void TestCryptoRoundTrip()
        {
            string sample = "协议自测-" + new string('A', 260);
            string encrypted = CryptoUtil.RsaEncrypt(sample);
            string decrypted = CryptoUtil.RsaDecrypt(encrypted);
            Assert("rsa_chunk_roundtrip", sample == decrypted);
        }

        private static void TestSignedUrl()
        {
            DateTime fixedTime = new DateTime(2026, 8, 16, 12, 34, 56, DateTimeKind.Unspecified);
            string nonce = "00112233445566778899aabbccddeeff";
            string first = CryptoUtil.BuildSignedUrl(ProtocolConstants.LoginVerify, fixedTime, nonce);
            string second = CryptoUtil.BuildSignedUrl(ProtocolConstants.LoginVerify, fixedTime, nonce);
            Assert("signature_deterministic", first == second);
            Assert("signature_shape", Regex.IsMatch(first, @"[?&]Signature=[0-9a-f]{40}$"));
            Assert("signature_path", first.Contains(ProtocolConstants.ApiPath + ProtocolConstants.LoginVerify));
        }

        private static void TestRedaction()
        {
            string value = Logger.Redact("password=abc token=ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 phone=13800138000");
            Assert("redact_password", !value.Contains("password=abc"));
            Assert("redact_long_secret", !value.Contains("ABCDEFGHIJKLMNOPQRSTUVWXYZ"));
            Assert("redact_phone", !value.Contains("13800138000"));
        }

        private static void TestEncryptedResponseHandling()
        {
            EcloudApiClient client = new EcloudApiClient("selftest-device");
            MethodInfo decode = typeof(EcloudApiClient).GetMethod("DecodeResponse", BindingFlags.Instance | BindingFlags.NonPublic);
            JavaScriptSerializer json = new JavaScriptSerializer();

            string success = json.Serialize(new Dictionary<string, object>
            {
                { "state", "OK" },
                { "message", "success" },
                { "body", new Dictionary<string, object> { { "value", "accepted" } } }
            });
            object decoded = decode.Invoke(client, new object[] { Envelope(json, success), "/selftest/success", "selftest-1" });
            Assert("response_success_message_allowed", JsonValue.String(JsonValue.AsDictionary(decoded), "value") == "accepted");

            string tokenResponse = json.Serialize(new Dictionary<string, object>
            {
                { "state", "OK" },
                { "body", new Dictionary<string, object> { { "accessToken", "selftest-access-token-value" } } }
            });
            decode.Invoke(client, new object[] { Envelope(json, tokenResponse), ProtocolConstants.LoginVerifyAccessTicket, "selftest-2" });
            Assert("response_token_installed", client.HasToken);

            string failure = json.Serialize(new Dictionary<string, object>
            {
                { "state", "ERROR" },
                { "errorCode", "SELFTEST_ERROR" },
                { "errorMessage", "expected failure" }
            });
            bool businessError = false;
            try
            {
                decode.Invoke(client, new object[] { Envelope(json, failure), "/selftest/failure", "selftest-3" });
            }
            catch (TargetInvocationException exception)
            {
                businessError = exception.InnerException is EcloudApiException;
            }
            Assert("response_business_error_detected", businessError);
        }

        private static void TestCsapCrypto()
        {
            const string vmid = "c0d88cfc-9135-4e24-8fe9-8a3e2af49172";
            const string timestamp = "1784210423300";
            string request = CsapCrypto.BuildRequestJson(vmid, 3, timestamp);
            string encryptedRequest = CsapCrypto.EncryptRequest(request);
            Assert("csap_request_encrypted", !string.IsNullOrEmpty(encryptedRequest) && encryptedRequest != request);

            const string connectPlain = "-p 5100 -k 12345678 --vmid c0d88cfc-9135-4e24-8fe9-8a3e2af49172 --hv6 2001:db8::1";
            string connectHex = CsapCrypto.EncodeConnectStringForSelfTest(connectPlain);
            Assert("csap_connectstr_roundtrip", CsapCrypto.DecodeConnectString(connectHex) == connectPlain);
        }

        private static void TestLoginPayloads()
        {
            Dictionary<string, object> send = LoginService.BuildStandaloneSmsRequest("13800138000");
            Assert("sms_send_payload_shape", send.Count == 2 &&
                Convert.ToString(send["mobile"]) == "13800138000" &&
                Convert.ToString(send["codeType"]) == "login");

            Dictionary<string, object> verify = LoginService.BuildStandaloneSmsLoginRequest("13800138000", "123456");
            Assert("sms_verify_payload_shape", verify.Count == 3 &&
                Convert.ToString(verify["mobile"]) == "13800138000" &&
                Convert.ToString(verify["verificationCode"]) == "123456" &&
                Convert.ToBoolean(verify["isNeedTemporaryDeviceSelection"]));
            Assert("sms_login_timeout_classified", ProtocolConstants.LoginPaths.Contains(ProtocolConstants.LoginVerifySms));
        }

        private static void TestPathBProtocol()
        {
            const string vmid = "c0d88cfc-9135-4e24-8fe9-8a3e2af49172";
            const string key = "12345678";
            const string plain = "client --hv6 2001:db8::1 -k 12345678 --vmid c0d88cfc-9135-4e24-8fe9-8a3e2af49172 -p 5100 --proxy-sport 60063 --mode test";
            ConnectParameters parameters = ConnectStringParser.Parse(plain);
            Assert("pathb_parse_complete", parameters.IsComplete && parameters.Vmid == vmid && parameters.Key == key);
            Assert("pathb_parse_ports", parameters.Port == 5100 && parameters.ProxySport == 60063);
            Assert("pathb_parse_flags", Array.IndexOf(parameters.FlagNames, "proxy-sport") >= 0 && Array.IndexOf(parameters.FlagNames, "hv6") >= 0);

            PathBPackets packets = PathBProtocol.BuildPackets(parameters);
            string authAscii = System.Text.Encoding.ASCII.GetString(packets.Auth220);
            string redqAscii = System.Text.Encoding.ASCII.GetString(packets.Redq163);
            Assert("pathb_template_sizes", packets.Ztec50.Length == 50 && packets.Auth220.Length == 220 && packets.Redq163.Length == 163);
            Assert("pathb_template_vmid", authAscii.Contains(vmid) && redqAscii.Contains(key + vmid));
            Assert("pathb_template_placeholder_replaced", !redqAscii.Contains("91723341c0d88cfc-9135-4e24-8fe9-8a3e2af49172"));

            byte[] heart = PathBProtocol.BuildSpiceFrame(81, 0x74, new byte[] { 0 }, 20);
            byte[] header = PathBProtocol.VendorHeader(heart.Length);
            byte[] wrapped = new byte[header.Length + heart.Length];
            Buffer.BlockCopy(header, 0, wrapped, 0, header.Length);
            Buffer.BlockCopy(heart, 0, wrapped, header.Length, heart.Length);
            List<PathBProtocol.SpiceFrame> frames = PathBProtocol.ParseVendorFrames(wrapped);
            Assert("pathb_heart_parse", frames.Count == 1 && frames[0].Type == 0x74 && frames[0].Serial == 81);
            Assert("pathb_heart_ack_shape", BitConverter.ToString(PathBProtocol.HeartAck(81)).Replace("-", string.Empty).ToLowerInvariant() == "5100000000000000790001000000000000000000");
            Assert("pathb_agent_heartbeat_shape", PathBProtocol.AgentHeartbeat(100).Length == 36);
        }

        private static void TestKeepAliveDefaults()
        {
            Assert("keepalive_heart_listen_default", KeepAliveService.HeartListenSeconds == 60);
            Assert("keepalive_round_interval_default", KeepAliveService.RoundIntervalSeconds == 300);
            PathBHandshakeResult result = new PathBHandshakeResult
            {
                ZtecOk = true,
                AuthOk = true,
                TlsOk = true,
                RedqOk = true,
                TicketOk = true,
                HeartCount = 2
            };
            Assert("keepalive_requires_two_hearts", result.HeartKeepAliveOk);
            result.HeartCount = 1;
            Assert("keepalive_rejects_one_heart", !result.HeartKeepAliveOk);
        }

        private static void TestCmssLaunchCrypto()
        {
            string keyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cmsszte-public.pem");
            Assert("cmss_public_key_present", File.Exists(keyPath));
            if (!File.Exists(keyPath)) return;

            string pem = File.ReadAllText(keyPath);
            MethodInfo parse = typeof(CmssLaunchService).GetMethod("ParsePublicKey", BindingFlags.Static | BindingFlags.NonPublic);
            System.Security.Cryptography.RSAParameters parameters =
                (System.Security.Cryptography.RSAParameters)parse.Invoke(null, new object[] { pem });
            Assert("cmss_public_key_shape", parameters.Modulus != null && parameters.Modulus.Length == 128 && parameters.Exponent != null);

            MethodInfo encrypt = typeof(CmssLaunchService).GetMethod("EncryptRsaChunked", BindingFlags.Static | BindingFlags.NonPublic);
            string cipher = (string)encrypt.Invoke(null, new object[] { "{\"vmid\":\"selftest\",\"padding\":\"" + new string('A', 260) + "\"}", pem });
            byte[] cipherBytes = Convert.FromBase64String(cipher);
            Assert("cmss_rsa_chunked_shape", cipherBytes.Length == 384);

            MethodInfo build = typeof(CmssLaunchService).GetMethod("BuildPlain", BindingFlags.Static | BindingFlags.NonPublic);
            EcloudLite.Models.Desktop desktop = new EcloudLite.Models.Desktop
            {
                MachineId = "selftest-machine",
                RawFields = new Dictionary<string, object>
                {
                    { "customLoginParams", new Dictionary<string, object> { { "shape", "safe" } } },
                    { "accessTicket", "must-not-log" },
                    { "isOpen", true }
                }
            };
            Dictionary<string, object> plain = (Dictionary<string, object>)build.Invoke(null, new object[] { desktop, 15900, "selftest-device" });
            Assert("cmss_plain_core", Convert.ToString(plain["vmid"]) == "selftest-machine" && Convert.ToString(plain["socketPort"]) == "15900");
            Assert("cmss_plain_mapping", plain.ContainsKey("vm_start") && plain.ContainsKey("accessTicket") && plain.ContainsKey("clientVersion") && plain.ContainsKey("deviceId"));
        }

        private static void TestMissingCmssRuntimeMessage()
        {
            string missingRoot = Path.Combine(Path.GetTempPath(), "EcloudLite-selftest-missing-runtime-" + Guid.NewGuid().ToString("N"));
            CmssLaunchService service = new CmssLaunchService(missingRoot, "selftest-device");
            EcloudLite.Models.Desktop desktop = new EcloudLite.Models.Desktop
            {
                MachineId = "selftest-machine",
                OriginCompanyCode = "CMSSZTE"
            };
            bool clearMessage = false;
            try
            {
                service.Launch(desktop, true);
            }
            catch (FileNotFoundException exception)
            {
                clearMessage = exception.Message.Contains("未找到官方 CMSS runtime") &&
                    exception.Message.Contains("登录、云电脑列表和会话管理仍可使用");
            }
            Assert("cmss_missing_runtime_message", clearMessage);
        }

        private static void TestRuntimeSetupSupport()
        {
            string root = Path.Combine(Path.GetTempPath(), "EcloudLite-selftest-runtime-" + Guid.NewGuid().ToString("N"));
            string runtime = Path.Combine(root, "cmss-runtime");
            try
            {
                string reason;
                Assert("runtime_setup_empty_rejected", !RuntimeSetupService.IsRuntimeReady(runtime, out reason));
                string[] required =
                {
                    Path.Combine("client", "uSmartView_VDI_Client.exe"),
                    Path.Combine("client", "vdconn.dll"),
                    Path.Combine("client", "BasicFunc.dll"),
                    Path.Combine("client", "platforms", "qwindows.dll"),
                    Path.Combine("client", "cmsszte-public.pem")
                };
                for (int i = 0; i < required.Length; i++)
                {
                    string file = Path.Combine(runtime, required[i]);
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllBytes(file, new byte[] { 1 });
                }
                Assert("runtime_setup_required_files_accepted", RuntimeSetupService.IsRuntimeReady(runtime, out reason));
                Assert("runtime_setup_official_url", RuntimeSetupService.OfficialDownloadPage.StartsWith("https://ecloud.10086.cn/", StringComparison.Ordinal));
                Assert("runtime_setup_7zip_url", RuntimeSetupService.SevenZipDownloadUrl.StartsWith("https://www.7-zip.org/", StringComparison.Ordinal));

                string executable = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                List<string> closure = PeDependencyResolver.Resolve(Path.GetDirectoryName(executable), new[] { executable }, null);
                Assert("runtime_setup_pe_parser", closure.Exists(delegate(string file) { return string.Equals(file, executable, StringComparison.OrdinalIgnoreCase); }));
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }

        private static void TestCmssControlServer()
        {
            CmssControlServer server = new CmssControlServer("selftest-machine", "CMSSZTE");
            List<string> toolbarActions = new List<string>();
            using (System.Threading.AutoResetEvent toolbarActionReceived = new System.Threading.AutoResetEvent(false))
            {
                server.ToolbarActionReceived += delegate(string action)
                {
                    lock (toolbarActions) toolbarActions.Add(action);
                    toolbarActionReceived.Set();
                };
            int port = server.Start();
            try
            {
                byte[] json = System.Text.Encoding.UTF8.GetBytes("{\"id\":\"selftest-machine\",\"companyCode\":\"CMSSZTE\",\"data\":null}");
                using (TcpClient client = new TcpClient("127.0.0.1", port))
                {
                    WriteCmssControlFrame(client, 1, json);
                    byte[] minimize = System.Text.Encoding.UTF8.GetBytes(
                        "{\"companyCode\":\"CMSSZTE\",\"id\":\"selftest-machine\",\"data\":{\"msg_type\":10,\"msg_data\":{\"action\":\"minimize\"}}}");
                    WriteCmssControlFrame(client, 1010, minimize);
                    Assert("cmss_control_minimize_event", toolbarActionReceived.WaitOne(1500));
                    byte[] quit = System.Text.Encoding.UTF8.GetBytes(
                        "{\"companyCode\":\"CMSSZTE\",\"id\":\"selftest-machine\",\"data\":{\"msg_type\":10,\"msg_data\":{\"action\":\"quit\"}}}");
                    WriteCmssControlFrame(client, 1010, quit);
                    Assert("cmss_control_quit_event", toolbarActionReceived.WaitOne(1500));
                }
                Assert("cmss_control_heart_received", server.HeartbeatCount == 1);
                lock (toolbarActions)
                {
                    Assert("cmss_control_toolbar_action_count", toolbarActions.Count == 2);
                    Assert("cmss_control_toolbar_actions", toolbarActions[0] == "minimize" && toolbarActions[1] == "quit");
                }
            }
            finally
            {
                server.Dispose();
            }
            }
        }

        private static void WriteCmssControlFrame(TcpClient client, uint type, byte[] json)
        {
            List<byte> inner = new List<byte>();
            inner.AddRange(BitConverter.GetBytes(type));
            inner.AddRange(BitConverter.GetBytes((uint)json.Length));
            inner.AddRange(json);
            List<byte> wire = new List<byte>();
            foreach (byte value in inner)
            {
                if (value == 0x0e) { wire.Add(0x0e); wire.Add(0x01); }
                else if (value == 0x0d) { wire.Add(0x0e); wire.Add(0x02); }
                else wire.Add(value);
            }
            wire.Add(0x0d);
            client.GetStream().Write(wire.ToArray(), 0, wire.Count);
        }

        private static void TestCmssSessionLifecycle()
        {
            string commandProcessor = Environment.GetEnvironmentVariable("COMSPEC");
            if (string.IsNullOrEmpty(commandProcessor))
            {
                Console.WriteLine("[SKIP] cmss_session_lifecycle (COMSPEC unavailable)");
                return;
            }
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = commandProcessor,
                Arguments = "/c exit 0",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process process = Process.Start(startInfo);
            CmssLaunchResult result = new CmssLaunchResult { Process = process, ProcessId = process.Id };
            using (System.Threading.ManualResetEvent ended = new System.Threading.ManualResetEvent(false))
            {
                result.Ended += delegate { ended.Set(); };
                result.StartMonitoring();
                Assert("cmss_session_end_event", ended.WaitOne(3000));
            }
            result.Dispose();
            result.Dispose();
            Assert("cmss_session_cleanup_idempotent", !result.IsRunning);
        }

        private static string Envelope(JavaScriptSerializer json, string plaintext)
        {
            return json.Serialize(new Dictionary<string, object>
            {
                { "params", CryptoUtil.RsaEncrypt(plaintext) }
            });
        }

        private static void TestProtectedSettings()
        {
            SettingsStore store = new SettingsStore();
            const string token = "selftest-token-7b5b93a3-4af9-4f3a-9c76-1d7a4b29a801";
            const string password = "selftest-password-4F!s9q";
            string protectedToken = store.ProtectToken(token);
            string protectedPassword = store.ProtectPassword(password);
            if (string.IsNullOrEmpty(protectedToken))
            {
                Console.WriteLine("[SKIP] dpapi_roundtrip (current user profile is unavailable)");
            }
            else
            {
                Assert("dpapi_ciphertext_differs", protectedToken != token);
                Assert("dpapi_roundtrip", store.UnprotectToken(protectedToken) == token);
                Assert("dpapi_password_ciphertext_differs", protectedPassword != password);
                Assert("dpapi_password_roundtrip", store.UnprotectPassword(protectedPassword) == password);
            }

            AppSettings settings = new AppSettings
            {
                Username = "13800138000",
                DeviceUid = "selftest-device",
                ProtectedToken = protectedToken,
                ProtectedPassword = protectedPassword,
                PasswordUsername = "13800138000",
                RememberPassword = true,
                AutoLogin = true,
                LoginMode = "password",
                SaveSession = true,
                SelectedSessionId = "session-password",
                SavedSessions = new List<SavedSession>
                {
                    new SavedSession
                    {
                        Id = "session-password",
                        Username = "13800138000",
                        LoginMode = "password",
                        ProtectedToken = protectedToken,
                        ProtectedPassword = protectedPassword,
                        UpdatedUtc = "2026-08-16T10:00:00.0000000Z"
                    },
                    new SavedSession
                    {
                        Id = "session-sms",
                        Username = "13900139000",
                        LoginMode = "sms",
                        ProtectedToken = protectedToken,
                        ProtectedPassword = string.Empty,
                        UpdatedUtc = "2026-08-16T10:01:00.0000000Z"
                    }
                },
                LastInstanceId = "CCA-selftest"
            };
            store.Save(settings);
            AppSettings loaded = store.Load();
            Assert("settings_roundtrip", loaded.Username == settings.Username && loaded.LastInstanceId == settings.LastInstanceId &&
                loaded.RememberPassword && loaded.AutoLogin && loaded.LoginMode == "password");
            Assert("saved_sessions_roundtrip", loaded.SaveSession && loaded.SelectedSessionId == "session-password" &&
                loaded.SavedSessions.Count == 2 && loaded.SavedSessions[0].Username == "13800138000" &&
                loaded.SavedSessions[1].LoginMode == "sms");
            Assert("saved_session_sms_has_no_password", string.IsNullOrEmpty(loaded.SavedSessions[1].ProtectedPassword));
            Assert("saved_session_display_state", loaded.SavedSessions[0].ToString().Contains("13800138000") &&
                loaded.SavedSessions[0].ToString().Contains("密码"));
            string settingsText = File.ReadAllText(AppPaths.Settings);
            Assert("settings_token_not_plaintext", !settingsText.Contains(token));
            Assert("settings_password_not_plaintext", !settingsText.Contains(password));
        }

        private static void TestSessionExpiryClassification()
        {
            MethodInfo classify = typeof(MainForm).GetMethod("IsSessionExpired", BindingFlags.Static | BindingFlags.NonPublic);
            Dictionary<string, object> empty = new Dictionary<string, object>();
            bool unauthorized = (bool)classify.Invoke(null, new object[] { new EcloudApiException("401", "unauthorized", empty) });
            bool tokenMessage = (bool)classify.Invoke(null, new object[] { new EcloudApiException("500", "Token 已过期", empty) });
            bool gateway = (bool)classify.Invoke(null, new object[] { new EcloudApiException("502", "bad gateway", empty) });
            Assert("session_expiry_401", unauthorized);
            Assert("session_expiry_message", tokenMessage);
            Assert("session_expiry_transient_rejected", !gateway);
        }

        private static void TestAppInfo()
        {
            Assert("about_lite_version", AppInfo.LiteVersion == "0.1.3");
            Assert("about_client_baseline", AppInfo.ClientBaseline == "V3.8.4.v22607211406");
            Assert("about_cloud_version", AppInfo.CloudComputerVersion == "V3.8.4.v2");
            Assert("about_desktop_protocol", AppInfo.DesktopProtocolVersion == "V11.250625/V1.2.251014/V2.3.2.0.0");
        }

        private static void TestFileRedaction()
        {
            const string marker = "selftest-secret-9a38d88b-58c1-4d40-9df4-7b1d6bb7c521";
            Logger.Info("SELFTEST", "password=plain-password token=" + marker + " phone=13800138000");
            string file = Logger.CurrentFile;
            bool readable = !string.IsNullOrEmpty(file) && File.Exists(file);
            string text = readable ? File.ReadAllText(file) : string.Empty;
            Assert("log_file_created", readable);
            Assert("log_secret_redacted", !text.Contains(marker) && !text.Contains("plain-password") && !text.Contains("13800138000"));
        }

        private static void Assert(string name, bool condition)
        {
            Console.WriteLine((condition ? "[PASS] " : "[FAIL] ") + name);
            if (!condition) _failures++;
        }
    }
}
