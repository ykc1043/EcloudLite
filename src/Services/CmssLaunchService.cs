using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using EcloudLite.Infrastructure;
using EcloudLite.Models;
using EcloudLite.Protocol;

namespace EcloudLite.Services
{
    internal sealed class CmssLaunchResult : IDisposable
    {
        private const int SwMinimize = 6;
        private const uint WmSysCommand = 0x0112;
        private const uint ScMinimize = 0xF020;
        private int _stopped;
        private int _endedRaised;
        private readonly object _stopGate = new object();
        public bool Success { get; set; }
        public string Message { get; set; }
        public int SocketPort { get; set; }
        public int PlainLength { get; set; }
        public string PlainSha16 { get; set; }
        public int CipherLength { get; set; }
        public int ProcessId { get; set; }
        public Process Process { get; set; }
        public Process ServiceAgentProcess { get; set; }
        public CmssControlServer ControlServer { get; set; }
        public string PortableProfileRoot { get; set; }
        public event EventHandler Ended;

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

        public bool IsRunning
        {
            get
            {
                try { return Process != null && !Process.HasExited; }
                catch { return false; }
            }
        }

        public void StartMonitoring()
        {
            Process process = Process;
            if (process == null) return;
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += NativeProcessExited;
                Logger.Info("CMSS", "native monitor attached pid=" + ProcessId + " has_exited=" + process.HasExited);
            }
            catch (Exception exception)
            {
                Logger.Exception("CMSS", exception, "native monitor attach failed pid=" + ProcessId);
            }
            Thread monitor = new Thread(new ThreadStart(delegate { MonitorLoop(process); }))
            {
                IsBackground = true,
                Name = "EcloudLite-CMSS-Native-Monitor"
            };
            monitor.Start();
        }

        public bool MinimizeWindow()
        {
            Process process = Process;
            if (process == null)
            {
                Logger.Warn("CMSS", "native minimize skipped; process is unavailable pid=" + ProcessId);
                return false;
            }
            try
            {
                process.Refresh();
                if (process.HasExited)
                {
                    Logger.Warn("CMSS", "native minimize skipped; renderer already exited pid=" + ProcessId);
                    return false;
                }
                IntPtr handle = GetMainWindowHandle();
                if (handle == IntPtr.Zero)
                {
                    Logger.Warn("CMSS", "native minimize failed; main window handle is zero pid=" + ProcessId);
                    return false;
                }
                bool shown = ShowWindowAsync(handle, SwMinimize);
                bool posted = shown || PostMessage(handle, WmSysCommand, new IntPtr(ScMinimize), IntPtr.Zero);
                Logger.Info("CMSS", "native minimize dispatched pid=" + ProcessId +
                    " hwnd=0x" + handle.ToInt64().ToString("x") +
                    " show_window=" + shown + " fallback_post=" + (!shown && posted) + " success=" + posted);
                return posted;
            }
            catch (Exception exception)
            {
                Logger.Exception("CMSS", exception, "native minimize failed pid=" + ProcessId);
                return false;
            }
        }

        public IntPtr GetMainWindowHandle()
        {
            Process process = Process;
            if (process == null) return IntPtr.Zero;
            try
            {
                process.Refresh();
                return process.HasExited ? IntPtr.Zero : process.MainWindowHandle;
            }
            catch (Exception exception)
            {
                Logger.Exception("CMSS", exception, "native main window handle lookup failed pid=" + ProcessId);
                return IntPtr.Zero;
            }
        }

        private void MonitorLoop(Process process)
        {
            for (int i = 0; i < 30 && Volatile.Read(ref _stopped) == 0; i++)
            {
                try
                {
                    if (process.HasExited)
                    {
                        Logger.Warn("CMSS", "native renderer exited early pid=" + ProcessId + " exit_code=" + process.ExitCode);
                        return;
                    }
                    IntPtr handle = IntPtr.Zero;
                    string title = string.Empty;
                    bool responding = false;
                    long workingSet = 0;
                    try { handle = process.MainWindowHandle; } catch { }
                    try { title = process.MainWindowTitle ?? string.Empty; } catch { }
                    try { responding = process.Responding; } catch { }
                    try { workingSet = process.WorkingSet64; } catch { }
                    Logger.Info("CMSS", "native state pid=" + ProcessId + " t=" + (i + 1) +
                        "s exited=false responding=" + responding + " hwnd=0x" + handle.ToInt64().ToString("x") +
                        " title_len=" + title.Length + " working_set=" + workingSet +
                        " hearts=" + (ControlServer == null ? 0 : ControlServer.HeartbeatCount));
                    Thread.Sleep(1000);
                }
                catch (Exception exception)
                {
                    if (Volatile.Read(ref _stopped) == 0)
                        Logger.Exception("CMSS", exception, "native monitor failed pid=" + ProcessId);
                    return;
                }
            }
        }

        private void NativeProcessExited(object sender, EventArgs e)
        {
            lock (_stopGate)
            {
                if (Volatile.Read(ref _stopped) != 0) return;
                Process exitedProcess = sender as Process;
                int exitCode = -1;
                try { if (exitedProcess != null) exitCode = exitedProcess.ExitCode; } catch { }
                Logger.Warn("CMSS", "native renderer exited pid=" + ProcessId + " exit_code=" + exitCode);
                Stop();
            }
        }

        public void Stop()
        {
            lock (_stopGate)
            {
                if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
                try { if (Process != null) Process.Exited -= NativeProcessExited; } catch { }
                StopProcess(Process, "native renderer", true);
                StopProcess(ServiceAgentProcess, "service agent", false);
                if (ControlServer != null) ControlServer.Dispose();
                CmssLaunchService.MirrorNativeRoamingConfig(PortableProfileRoot);
                NotifyEnded();
            }
        }

        private void NotifyEnded()
        {
            if (Interlocked.Exchange(ref _endedRaised, 1) != 0) return;
            EventHandler handler = Ended;
            if (handler == null) return;
            try { handler(this, EventArgs.Empty); }
            catch (Exception exception) { Logger.Exception("CMSS", exception, "native session ended callback failed pid=" + ProcessId); }
        }

        private static void StopProcess(Process process, string label, bool graceful)
        {
            if (process == null) return;
            int pid = 0;
            try { pid = process.Id; } catch { }
            try
            {
                if (!process.HasExited)
                {
                    Logger.Info("CMSS", "stopping " + label + " pid=" + pid + " graceful=" + graceful);
                    if (graceful)
                    {
                        try { if (process.MainWindowHandle != IntPtr.Zero) process.CloseMainWindow(); } catch { }
                        process.WaitForExit(2000);
                    }
                    if (!process.HasExited)
                    {
                        Logger.Warn("CMSS", label + " still running; terminating exact pid=" + pid);
                        process.Kill();
                        process.WaitForExit(2000);
                    }
                }
                Logger.Info("CMSS", label + " cleanup complete pid=" + pid + " exited=" + process.HasExited);
            }
            catch (Exception exception) { Logger.Exception("CMSS", exception, label + " cleanup failed pid=" + pid); }
            finally { try { process.Close(); } catch { } }
        }

        public void Dispose() { Stop(); }
    }

    internal sealed class CmssLaunchService
    {
        private const string ClientExe = "uSmartView_VDI_Client.exe";
        private const string PublicKeyFile = "cmsszte-public.pem";
        private const int RsaPlainBlock = 117;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private readonly string _runtimeDirectory;
        private readonly string _deviceId;

        public CmssLaunchService(string runtimeDirectory, string deviceId = null)
        {
            _runtimeDirectory = runtimeDirectory;
            _deviceId = deviceId ?? string.Empty;
        }

        public CmssLaunchResult Launch(Desktop desktop, bool allowLive)
        {
            if (!allowLive) throw new InvalidOperationException("CMSS 启动默认拒绝，必须由用户确认");
            if (desktop == null) throw new ArgumentNullException("desktop");
            if (!string.Equals(desktop.OriginCompanyCode, "CMSSZTE", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("当前仅支持 CMSSZTE 原生渲染器");

            string clientDirectory = Path.Combine(_runtimeDirectory, "client");
            string clientPath = Path.Combine(clientDirectory, ClientExe);
            string keyPath = Path.Combine(clientDirectory, PublicKeyFile);
            if (!File.Exists(clientPath))
                throw new FileNotFoundException(
                    "未找到官方 CMSS runtime。EcloudLite 的登录、云电脑列表和会话管理仍可使用；启动云电脑前，请从官方安装包自行取得 runtime 并按文档完成本地配置。",
                    clientPath);
            if (!File.Exists(keyPath)) throw new FileNotFoundException("CMSS 最小运行时缺少公钥", keyPath);
            LogRuntimePreflight(clientDirectory);

            CmssControlServer controlServer = new CmssControlServer(desktop.MachineId, desktop.OriginCompanyCode);
            Process serviceAgent = null;
            int socketPort = controlServer.Start();
            try
            {
                string profileRoot = PreparePortableProfile(clientDirectory);
                string serviceAgentPath = Path.Combine(clientDirectory, "uSmartViewServiceAgent.exe");
                if (File.Exists(serviceAgentPath))
                {
                    ProcessStartInfo agentInfo = new ProcessStartInfo
                    {
                        FileName = serviceAgentPath,
                        WorkingDirectory = clientDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    ApplyPortableEnvironment(agentInfo, profileRoot);
                    serviceAgent = Process.Start(agentInfo);
                    if (serviceAgent != null)
                    {
                        Logger.Info("CMSS", "service agent started pid=" + serviceAgent.Id + " exe=" + serviceAgentPath);
                        Thread.Sleep(300);
                        try
                        {
                            Logger.Info("CMSS", "service agent state pid=" + serviceAgent.Id + " exited=" + serviceAgent.HasExited +
                                (serviceAgent.HasExited ? " exit_code=" + serviceAgent.ExitCode : string.Empty));
                        }
                        catch { }
                    }
                }
                else Logger.Warn("CMSS", "service agent missing; continuing without it path=" + serviceAgentPath);
                Dictionary<string, object> plainObject = BuildPlain(desktop, socketPort, _deviceId);
                string plainJson = _json.Serialize(plainObject);
                string pem = File.ReadAllText(keyPath, Encoding.ASCII);
                string cipher = EncryptRsaChunked(plainJson, pem);
                Logger.Info("CMSS", "launch prepared origin=CMSSZTE machine=" + Logger.ShortId(desktop.MachineId) +
                    " socket_port=" + socketPort + " plain_len=" + plainJson.Length +
                    " plain_sha16=" + CryptoUtil.Sha256Hex(plainJson).Substring(0, 16) +
                    " cipher_len=" + cipher.Length + " fields=" + string.Join(",", plainObject.Keys.OrderBy(delegate(string value) { return value; }, StringComparer.Ordinal)));

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = clientPath,
                    Arguments = "--json " + cipher,
                    WorkingDirectory = clientDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };
                ApplyPortableEnvironment(startInfo, profileRoot);
                Process process = Process.Start(startInfo);
                if (process == null) throw new InvalidOperationException("渲染器进程未能启动");
                Logger.Info("CMSS", "native renderer started pid=" + process.Id + " exe=" + clientPath + " socket_port=" + socketPort);
                CmssLaunchResult result = new CmssLaunchResult
                {
                    Success = true,
                    Message = "CMSS 渲染器已启动",
                    SocketPort = socketPort,
                    PlainLength = plainJson.Length,
                    PlainSha16 = CryptoUtil.Sha256Hex(plainJson).Substring(0, 16),
                    CipherLength = cipher.Length,
                    ProcessId = process.Id,
                    Process = process,
                    ServiceAgentProcess = serviceAgent,
                    ControlServer = controlServer,
                    PortableProfileRoot = profileRoot
                };
                result.StartMonitoring();
                return result;
            }
            catch
            {
                if (serviceAgent != null)
                {
                    try { if (!serviceAgent.HasExited) serviceAgent.Kill(); } catch { }
                    try { serviceAgent.Close(); } catch { }
                }
                controlServer.Dispose();
                throw;
            }
        }

        private static string PreparePortableProfile(string clientDirectory)
        {
            string runtimeDirectory = Directory.GetParent(clientDirectory).FullName;
            string profileRoot = Path.Combine(runtimeDirectory, "profile");
            string roaming = Path.Combine(profileRoot, "Roaming");
            string local = Path.Combine(profileRoot, "Local");
            string temp = Path.Combine(profileRoot, "Temp");
            string shippedConfig = Path.Combine(runtimeDirectory, "config");
            string profileConfig = Path.Combine(roaming, "Ecloud-Cloud-Computer-Application", "AllUsers", "CloudComputer_C", "config");
            Directory.CreateDirectory(roaming);
            Directory.CreateDirectory(local);
            Directory.CreateDirectory(temp);
            if (Directory.Exists(shippedConfig))
                CopyMissingFiles(shippedConfig, profileConfig);
            Logger.Info("CMSS", "portable profile root=" + profileRoot +
                " appdata=" + roaming + " localappdata=" + local + " temp=" + temp +
                " config=" + profileConfig);
            return profileRoot;
        }

        private static void CopyMissingFiles(string sourceDirectory, string targetDirectory)
        {
            Directory.CreateDirectory(targetDirectory);
            string[] files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
            int copied = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string relative = files[i].Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.Combine(targetDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (!File.Exists(target))
                {
                    File.Copy(files[i], target);
                    copied++;
                }
            }
            Logger.Info("CMSS", "portable config prepared source=" + sourceDirectory +
                " target=" + targetDirectory + " files=" + files.Length + " copied=" + copied);
        }

        internal static void MirrorNativeRoamingConfig(string profileRoot)
        {
            if (string.IsNullOrEmpty(profileRoot)) return;
            try
            {
                string relative = Path.Combine("Ecloud-Cloud-Computer-Application", "AllUsers", "CloudComputer_C", "config");
                string source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), relative);
                string target = Path.Combine(profileRoot, "Roaming", relative);
                if (!Directory.Exists(source))
                {
                    Logger.Info("CMSS", "native roaming config mirror skipped; source_missing=" + source);
                    return;
                }
                Directory.CreateDirectory(target);
                string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
                int copied = 0;
                for (int i = 0; i < files.Length; i++)
                {
                    string fileRelative = files[i].Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string destination = Path.Combine(target, fileRelative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(files[i], destination, true);
                    copied++;
                }
                Logger.Info("CMSS", "native roaming config mirrored source=" + source +
                    " target=" + target + " files=" + copied);
            }
            catch (Exception exception)
            {
                Logger.Exception("CMSS", exception, "native roaming config mirror failed");
            }
        }

        private static void ApplyPortableEnvironment(ProcessStartInfo startInfo, string profileRoot)
        {
            string roaming = Path.Combine(profileRoot, "Roaming");
            string local = Path.Combine(profileRoot, "Local");
            string temp = Path.Combine(profileRoot, "Temp");
            startInfo.EnvironmentVariables["APPDATA"] = roaming;
            startInfo.EnvironmentVariables["LOCALAPPDATA"] = local;
            startInfo.EnvironmentVariables["TEMP"] = temp;
            startInfo.EnvironmentVariables["TMP"] = temp;
            Logger.Info("CMSS", "portable environment applied appdata=" + roaming +
                " localappdata=" + local + " temp=" + temp);
        }

        private static void LogRuntimePreflight(string clientDirectory)
        {
            string[] requiredFiles =
            {
                ClientExe,
                "uSmartViewServiceAgent.exe",
                "vdconn.dll",
                "EncryptDll.dll",
                "CagVpnTool.dll",
                "libcag.dll",
                "iClassProxy.dll",
                "usmartviewservice.dll",
                "libcrypto-1_1.dll",
                "libcrypto-3.dll"
            };
            List<string> missing = new List<string>();
            for (int i = 0; i < requiredFiles.Length; i++)
            {
                string path = Path.Combine(clientDirectory, requiredFiles[i]);
                if (!File.Exists(path))
                {
                    missing.Add(requiredFiles[i]);
                    continue;
                }
                FileInfo file = new FileInfo(path);
                Logger.Info("CMSS", "runtime file name=" + requiredFiles[i] +
                    " bytes=" + file.Length + " modified_utc=" + file.LastWriteTimeUtc.ToString("O"));
            }
            string runtimeDirectory = Directory.GetParent(clientDirectory).FullName;
            string manifestPath = Path.Combine(runtimeDirectory, "runtime-manifest.sha256");
            Logger.Info("CMSS", "runtime preflight root=" + runtimeDirectory +
                " manifest=" + File.Exists(manifestPath) + " missing=" +
                (missing.Count == 0 ? "none" : string.Join(",", missing.ToArray())) +
                " native_log_expected=" + Path.GetFullPath(Path.Combine(clientDirectory, "..", "..", "log", "client.log")));
            if (missing.Count != 0)
                throw new FileNotFoundException("CMSS 最小运行时缺少关键动态依赖: " + string.Join(", ", missing.ToArray()));
        }

        private static Dictionary<string, object> BuildPlain(Desktop desktop, int socketPort, string deviceId = null)
        {
            Dictionary<string, object> source = desktop.RawFields == null
                ? new Dictionary<string, object>(StringComparer.Ordinal)
                : new Dictionary<string, object>(desktop.RawFields, StringComparer.Ordinal);
            Dictionary<string, object> plain = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "vmid", desktop.MachineId ?? string.Empty },
                { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
                { "socketPort", socketPort.ToString() }
            };
            string[] direct =
            {
                "adUser", "adPassword", "customParams", "customLoginParams", "customPrivateLoginParams",
                "forcePreemption", "isThinClient", "vmName", "httpProxyParams", "operatePolicys",
                "perssionObject", "userInfo", "osVersion", "desktopname", "isSpecialLine",
                "isShowCooperate", "virtualAppParams", "clientVersion", "clientType", "accessTicket",
                "updateReqUrl", "deviceId", "isDev", "connectSession", "desktopStatus", "watchMode", "adDomain"
            };
            for (int i = 0; i < direct.Length; i++)
            {
                object value;
                if (source.TryGetValue(direct[i], out value) && value != null) plain[direct[i]] = value;
            }
            if (!plain.ContainsKey("clientVersion")) plain["clientVersion"] = "3.8.4";
            if (!plain.ContainsKey("vmName") && !string.IsNullOrEmpty(desktop.MachineName)) plain["vmName"] = desktop.MachineName;
            if (!plain.ContainsKey("desktopname") && !string.IsNullOrEmpty(desktop.MachineName)) plain["desktopname"] = desktop.MachineName;
            if (!plain.ContainsKey("deviceId") && !string.IsNullOrEmpty(deviceId)) plain["deviceId"] = deviceId;
            object isOpen;
            if (source.TryGetValue("isOpen", out isOpen) && isOpen != null) plain["vm_start"] = isOpen;
            else if (source.TryGetValue("vm_start", out isOpen) && isOpen != null) plain["vm_start"] = isOpen;
            return plain;
        }

        private static string EncryptRsaChunked(string plaintext, string pem)
        {
            RSAParameters parameters = ParsePublicKey(pem);
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(1024))
            {
                rsa.ImportParameters(parameters);
                byte[] input = Encoding.UTF8.GetBytes(plaintext);
                List<byte> output = new List<byte>();
                for (int offset = 0; offset < input.Length; offset += RsaPlainBlock)
                {
                    int length = Math.Min(RsaPlainBlock, input.Length - offset);
                    byte[] block = new byte[length];
                    Buffer.BlockCopy(input, offset, block, 0, length);
                    output.AddRange(rsa.Encrypt(block, false));
                }
                return Convert.ToBase64String(output.ToArray());
            }
        }

        private static RSAParameters ParsePublicKey(string pem)
        {
            string body = pem.Replace("-----BEGIN PUBLIC KEY-----", string.Empty)
                .Replace("-----END PUBLIC KEY-----", string.Empty)
                .Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
            byte[] der = Convert.FromBase64String(body);
            int offset = 0;
            byte[] outer = ReadElement(der, ref offset, 0x30);
            int outerOffset = 0;
            ReadElement(outer, ref outerOffset, 0x30);
            byte[] bitString = ReadElement(outer, ref outerOffset, 0x03);
            if (bitString.Length < 2 || bitString[0] != 0) throw new CryptographicException("公钥位串无效");
            int inner = 1;
            byte[] rsaSequence = ReadElement(bitString, ref inner, 0x30);
            int rsaOffset = 0;
            byte[] modulus = TrimInteger(ReadElement(rsaSequence, ref rsaOffset, 0x02));
            byte[] exponent = TrimInteger(ReadElement(rsaSequence, ref rsaOffset, 0x02));
            return new RSAParameters { Modulus = modulus, Exponent = exponent };
        }

        private static byte[] ReadElement(byte[] data, ref int offset, byte expected)
        {
            if (offset >= data.Length || data[offset++] != expected) throw new CryptographicException("公钥 DER 标签无效");
            int length = ReadLength(data, ref offset);
            if (length < 0 || offset + length > data.Length) throw new CryptographicException("公钥 DER 长度无效");
            byte[] value = new byte[length];
            Buffer.BlockCopy(data, offset, value, 0, length);
            offset += length;
            return value;
        }

        private static int ReadLength(byte[] data, ref int offset)
        {
            if (offset >= data.Length) throw new CryptographicException("公钥 DER 长度缺失");
            int first = data[offset++];
            if ((first & 0x80) == 0) return first;
            int count = first & 0x7f;
            if (count == 0 || count > 4 || offset + count > data.Length) throw new CryptographicException("公钥 DER 长度格式无效");
            int length = 0;
            for (int i = 0; i < count; i++) length = (length << 8) | data[offset++];
            return length;
        }

        private static byte[] TrimInteger(byte[] value)
        {
            int offset = 0;
            while (offset + 1 < value.Length && value[offset] == 0) offset++;
            byte[] result = new byte[value.Length - offset];
            Buffer.BlockCopy(value, offset, result, 0, result.Length);
            return result;
        }
    }

    internal sealed class CmssControlServer : IDisposable
    {
        private readonly string _machineId;
        private readonly string _companyCode;
        private readonly object _gate = new object();
        private readonly List<TcpClient> _clients = new List<TcpClient>();
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _stopping;
        private int _heartbeats;

        public event Action<string> ToolbarActionReceived;

        public CmssControlServer(string machineId, string companyCode)
        {
            _machineId = machineId ?? string.Empty;
            _companyCode = companyCode ?? string.Empty;
        }

        public int HeartbeatCount { get { return Volatile.Read(ref _heartbeats); } }

        public int Start()
        {
            Exception last = null;
            for (int port = 15900; port < 15964; port++)
            {
                TcpListener candidate = new TcpListener(IPAddress.Loopback, port);
                try
                {
                    candidate.Start(16);
                    _listener = candidate;
                    _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "EcloudLite-CMSS-Control" };
                    _acceptThread.Start();
                    Logger.Info("CMSSCTRL", "control server listening host=127.0.0.1 port=" + port + " machine=" + Logger.ShortId(_machineId));
                    return port;
                }
                catch (SocketException exception)
                {
                    last = exception;
                    try { candidate.Stop(); } catch { }
                }
            }
            throw new InvalidOperationException("无法绑定本地 CMSS 控制端口 15900-15963", last);
        }

        private void AcceptLoop()
        {
            while (!_stopping)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    if (_stopping) { client.Close(); break; }
                    lock (_gate) _clients.Add(client);
                    Logger.Info("CMSSCTRL", "native control connection accepted remote=" + client.Client.RemoteEndPoint);
                    Thread worker = new Thread(new ThreadStart(delegate { ClientLoop(client); })) { IsBackground = true, Name = "EcloudLite-CMSS-Control-Client" };
                    worker.Start();
                }
                catch (SocketException) { if (!_stopping) Logger.Warn("CMSSCTRL", "control accept socket error"); }
                catch (ObjectDisposedException) { break; }
                catch (Exception exception) { if (!_stopping) Logger.Exception("CMSSCTRL", exception, "control accept failed"); }
            }
        }

        private void ClientLoop(TcpClient client)
        {
            List<byte> frame = new List<byte>();
            bool escaped = false;
            byte[] buffer = new byte[4096];
            try
            {
                NetworkStream stream = client.GetStream();
                while (!_stopping)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    for (int i = 0; i < read; i++)
                    {
                        byte value = buffer[i];
                        if (escaped)
                        {
                            frame.Add(value == 1 ? (byte)0x0e : value == 2 ? (byte)0x0d : value);
                            escaped = false;
                        }
                        else if (value == 0x0e) escaped = true;
                        else if (value == 0x0d)
                        {
                            ProcessFrame(frame.ToArray());
                            frame.Clear();
                        }
                        else frame.Add(value);
                    }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception exception) { if (!_stopping) Logger.Exception("CMSSCTRL", exception, "control client loop failed"); }
            finally
            {
                lock (_gate) _clients.Remove(client);
                try { client.Close(); } catch { }
                Logger.Info("CMSSCTRL", "native control connection closed hearts=" + _heartbeats);
            }
        }

        private void ProcessFrame(byte[] frame)
        {
            if (frame == null || frame.Length < 8)
            {
                Logger.Debug("CMSSCTRL", "short control frame bytes=" + (frame == null ? 0 : frame.Length));
                return;
            }
            uint type = BitConverter.ToUInt32(frame, 0);
            uint jsonLength = BitConverter.ToUInt32(frame, 4);
            int availableJsonBytes = frame.Length - 8;
            if (jsonLength > availableJsonBytes)
            {
                Logger.Warn("CMSSCTRL", "invalid control frame type=" + type + " frame_bytes=" + frame.Length +
                    " json_len=" + jsonLength + " available_json_bytes=" + availableJsonBytes);
                return;
            }
            if (type == 1) Interlocked.Increment(ref _heartbeats);
            Logger.Info("CMSSCTRL", "control message type=" + type + " frame_bytes=" + frame.Length + " json_len=" + jsonLength +
                " heart_count=" + _heartbeats + " machine=" + Logger.ShortId(_machineId) + " company=" + _companyCode);
            if (type == 1010 && jsonLength > 0)
                ProcessToolbarMessage(frame, (int)jsonLength, availableJsonBytes);
        }

        private void ProcessToolbarMessage(byte[] frame, int jsonLength, int availableJsonBytes)
        {
            try
            {
                string jsonText = Encoding.UTF8.GetString(frame, 8, jsonLength);
                JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                Dictionary<string, object> root = json.Deserialize<Dictionary<string, object>>(jsonText);
                Dictionary<string, object> data = GetDictionary(root, "data");
                Dictionary<string, object> messageData = GetDictionary(data, "msg_data");
                int messageType = GetInt32(data, "msg_type", -1);
                string action = GetString(messageData, "action");
                Logger.Info("CMSSCTRL", "toolbar message parsed msg_type=" + messageType +
                    " action=" + (string.IsNullOrEmpty(action) ? "(empty)" : action) +
                    " json_bytes=" + jsonLength + " trailing_bytes=" + (availableJsonBytes - jsonLength));
                if (messageType != 10 || string.IsNullOrWhiteSpace(action)) return;

                action = action.Trim().ToLowerInvariant();
                Logger.Info("CMSSCTRL", "toolbar action received action=" + action +
                    " machine=" + Logger.ShortId(_machineId) + " company=" + _companyCode);
                Action<string> handler = ToolbarActionReceived;
                if (handler != null) handler(action);
            }
            catch (Exception exception)
            {
                Logger.Exception("CMSSCTRL", exception, "toolbar message parse failed json_bytes=" + jsonLength);
            }
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> source, string key)
        {
            if (source == null) return null;
            object value;
            return source.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            if (source == null) return string.Empty;
            object value;
            return source.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : string.Empty;
        }

        private static int GetInt32(Dictionary<string, object> source, string key, int fallback)
        {
            if (source == null) return fallback;
            object value;
            if (!source.TryGetValue(key, out value) || value == null) return fallback;
            try { return Convert.ToInt32(value); }
            catch { return fallback; }
        }

        public void Dispose()
        {
            if (_stopping) return;
            _stopping = true;
            try { if (_listener != null) _listener.Stop(); } catch { }
            lock (_gate)
            {
                foreach (TcpClient client in _clients.ToArray()) try { client.Close(); } catch { }
                _clients.Clear();
            }
            if (_acceptThread != null && _acceptThread.IsAlive) _acceptThread.Join(1500);
            Logger.Info("CMSSCTRL", "control server stopped hearts=" + _heartbeats);
        }
    }
}
