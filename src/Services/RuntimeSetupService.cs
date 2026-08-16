using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using EcloudLite.Infrastructure;

namespace EcloudLite.Services
{
    internal sealed class RuntimeSetupResult
    {
        public string RuntimeDirectory { get; set; }
        public string BackupDirectory { get; set; }
        public int FileCount { get; set; }
        public long Bytes { get; set; }
    }

    internal sealed class RuntimeSetupService
    {
        public const string OfficialDownloadPage = "https://ecloud.10086.cn/api/query/clouddesktop/ccaorder/#/downloadAppPage";
        public const string SevenZipDownloadUrl = "https://www.7-zip.org/a/7z2501-x64.msi";
        public const string SevenZipDownloadName = "7z2501-x64.msi";

        private const string CmssPublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQC5dwvTHYehc3BMwFBcZXBzrEKc
EacBeOw7k1BcGy9fv+UhFgL92ENpEqz5dLUEmqpGleGn3fH6VAdWUOS9/8u9kdS3
xlu4DSpAyN7cGNG8LThZST7g8rsNdsmPv7CrT5I4M93Jtl2psTqRYV64CbroCOVy
2z4QdKrmokSv3SNu+wIDAQAB
-----END PUBLIC KEY-----";

        private static readonly string[] PluginDirectories =
        {
            "platforms", "styles", "imageformats", "iconengines", "audio", "mediaservice",
            "bearer", "data", "guide", "sqldrivers", "win32", "win64", "Winsock"
        };

        private static readonly string[] ServiceScanNames =
        {
            "uSmartViewServiceAgent.exe", "usmartviewservice.dll", "iClassProxy.dll",
            "vdconn.dll", "EncryptDll.dll", "libcag.dll"
        };

        private static readonly string[] ResourceNames =
        {
            "desktop_switch.xml", "ErrorCodeDictionary.xml", "login_logo_right.png",
            "login_logo_rightdefault.png", "Microsoft.VC90.MFC.manifest", "rsa_pub.txt",
            "systeminfo.txt", "userAscriptionInfo.xml", "uSmartView.ico", "vdi_audio.wav",
            "VERSION", "vpn_CAG_new.xml", "vpn_CAG_ZTE.xml", "ztencr"
        };

        private static readonly string[] DynamicRuntimeNames =
        {
            "vdconn.dll", "BasicFunc.dll", "iClassProxy.dll", "libcag.dll", "usbRedirectCheck.dll",
            "serialMsgLib.dll", "netdetect.dll", "TipTranslator.dll", "EncryptDll.dll", "libvdisk.dll",
            "usbMsgLib.dll", "usmartviewservice.dll", "uSmartViewServiceAgent.exe",
            "IntelligentQA.exe", "UapAgent.exe"
        };

        private readonly string _applicationDirectory;
        private readonly string _runtimeDirectory;

        public RuntimeSetupService(string applicationDirectory)
        {
            _applicationDirectory = Path.GetFullPath(applicationDirectory);
            _runtimeDirectory = Path.Combine(_applicationDirectory, "cmss-runtime");
        }

        public string RuntimeDirectory { get { return _runtimeDirectory; } }

        public static bool IsRuntimeReady(string runtimeDirectory, out string reason)
        {
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
                string path = Path.Combine(runtimeDirectory, required[i]);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    reason = "缺少 " + required[i];
                    return false;
                }
            }
            reason = "runtime 已就绪";
            return true;
        }

        public static string FindSevenZip()
        {
            List<string> candidates = new List<string>();
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "7zip", "7z.exe"));
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(programFiles)) candidates.Add(Path.Combine(programFiles, "7-Zip", "7z.exe"));
            if (!string.IsNullOrEmpty(programFilesX86)) candidates.Add(Path.Combine(programFilesX86, "7-Zip", "7z.exe"));
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] pathEntries = pathValue.Split(Path.PathSeparator);
            for (int i = 0; i < pathEntries.Length; i++)
                if (!string.IsNullOrWhiteSpace(pathEntries[i])) candidates.Add(Path.Combine(pathEntries[i].Trim(), "7z.exe"));
            for (int i = 0; i < candidates.Count; i++)
            {
                try { if (File.Exists(candidates[i])) return Path.GetFullPath(candidates[i]); }
                catch { }
            }
            return string.Empty;
        }

        public static string SevenZipInstallerPath()
        {
            string directory = Path.Combine(AppPaths.Root, "tools", "downloads");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, SevenZipDownloadName);
        }

        public RuntimeSetupResult Build(string installerPath, string sevenZipPath, Action<string> progress)
        {
            ValidateInputs(installerPath, sevenZipPath);
            EnsureFreeSpace(AppPaths.Root, 2L * 1024 * 1024 * 1024);
            EnsureFreeSpace(_applicationDirectory, 220L * 1024 * 1024);

            string id = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string workRoot = Path.Combine(AppPaths.Root, "runtime-setup", id);
            string outerDirectory = Path.Combine(workRoot, "outer");
            string payloadDirectory = Path.Combine(workRoot, "payload");
            string stagedRuntime = Path.Combine(_applicationDirectory, "cmss-runtime.build-" + id);
            Directory.CreateDirectory(outerDirectory);
            Directory.CreateDirectory(payloadDirectory);
            Directory.CreateDirectory(stagedRuntime);
            bool success = false;
            try
            {
                Report(progress, "工作目录：" + workRoot);
                Report(progress, "阶段 1/5：从官方安装包提取 app.7z");
                RunSevenZip(sevenZipPath, "e " + Quote(installerPath) + " -o" + Quote(outerDirectory) + " -y -bso0 -bsp0 -bse1 app.7z", progress);
                string appArchive = Path.Combine(outerDirectory, "app.7z");
                if (!File.Exists(appArchive) || new FileInfo(appArchive).Length < 100L * 1024 * 1024)
                    throw new InvalidDataException("安装包中未找到有效的 app.7z；请选择兼容的移动云电脑 Windows 安装包");
                Report(progress, "app.7z 已提取，大小 " + Math.Round(new FileInfo(appArchive).Length / 1048576.0, 2) + " MiB");

                Report(progress, "阶段 2/5：只提取 drivers\\CMSS 运行组件");
                string extractionArguments = "x " + Quote(appArchive) + " -o" + Quote(payloadDirectory) +
                    " -y -bso0 -bsp0 -bse1 " +
                    Quote("drivers\\CMSS\\client\\*") + " " +
                    Quote("drivers\\CMSS\\config\\*") + " " +
                    Quote("drivers\\CMSS\\redirect\\clipboard\\*") + " " +
                    Quote("drivers\\CMSS\\updateinfo.ini");
                RunSevenZip(sevenZipPath, extractionArguments, progress);
                string cmssRoot = Path.Combine(payloadDirectory, "drivers", "CMSS");
                string sourceClient = Path.Combine(cmssRoot, "client");
                string entry = Path.Combine(sourceClient, "uSmartView_VDI_Client.exe");
                if (!File.Exists(entry)) throw new InvalidDataException("提取结果缺少 CMSS renderer，安装包版本可能不兼容");

                Report(progress, "阶段 3/5：扫描 PE 依赖闭包");
                string stagedClient = Path.Combine(stagedRuntime, "client");
                Directory.CreateDirectory(stagedClient);
                List<string> scanEntries = new List<string> { entry };
                AddExistingEntries(scanEntries, sourceClient, ServiceScanNames);
                for (int i = 0; i < PluginDirectories.Length; i++)
                {
                    string pluginDirectory = Path.Combine(sourceClient, PluginDirectories[i]);
                    if (!Directory.Exists(pluginDirectory)) continue;
                    string[] pluginDlls = Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.AllDirectories);
                    for (int j = 0; j < pluginDlls.Length; j++) scanEntries.Add(pluginDlls[j]);
                }
                List<string> closure = PeDependencyResolver.Resolve(sourceClient, scanEntries, progress);
                for (int i = 0; i < closure.Count; i++) CopyRelativeFile(sourceClient, closure[i], stagedClient);
                Report(progress, "PE 静态依赖文件：" + closure.Count);

                Report(progress, "阶段 4/5：复制动态组件、插件和配置");
                for (int i = 0; i < PluginDirectories.Length; i++)
                {
                    string source = Path.Combine(sourceClient, PluginDirectories[i]);
                    if (Directory.Exists(source)) CopyDirectory(source, Path.Combine(stagedClient, PluginDirectories[i]));
                }
                CopyExistingFiles(sourceClient, stagedClient, ResourceNames, progress, false);
                CopyExistingFiles(sourceClient, stagedClient, DynamicRuntimeNames, progress, true);
                string canonicalPem = CmssPublicKeyPem.Replace("\r\n", "\n").Replace("\n", Environment.NewLine) + Environment.NewLine;
                File.WriteAllText(Path.Combine(stagedClient, "cmsszte-public.pem"), canonicalPem, new UTF8Encoding(false));
                string sourceConfig = Path.Combine(cmssRoot, "config");
                if (Directory.Exists(sourceConfig)) CopyDirectory(sourceConfig, Path.Combine(stagedRuntime, "config"));
                string sourceRedirect = Path.Combine(cmssRoot, "redirect", "clipboard");
                if (Directory.Exists(sourceRedirect)) CopyDirectory(sourceRedirect, Path.Combine(stagedRuntime, "redirect", "clipboard"));
                string updateInfo = Path.Combine(cmssRoot, "updateinfo.ini");
                if (File.Exists(updateInfo)) File.Copy(updateInfo, Path.Combine(stagedRuntime, "updateinfo.ini"), true);
                Directory.CreateDirectory(Path.Combine(stagedRuntime, "log"));

                Report(progress, "阶段 5/5：校验并安装 runtime");
                string reason;
                if (!IsRuntimeReady(stagedRuntime, out reason)) throw new InvalidDataException("runtime 校验失败：" + reason);
                WriteManifest(stagedRuntime);
                string backup = InstallStagedRuntime(stagedRuntime, id, progress);
                string[] installedFiles = Directory.GetFiles(_runtimeDirectory, "*", SearchOption.AllDirectories);
                long bytes = 0;
                for (int i = 0; i < installedFiles.Length; i++) bytes += new FileInfo(installedFiles[i]).Length;
                Report(progress, "runtime 安装完成：文件 " + installedFiles.Length + "，大小 " + Math.Round(bytes / 1048576.0, 2) + " MiB");
                success = true;
                return new RuntimeSetupResult
                {
                    RuntimeDirectory = _runtimeDirectory,
                    BackupDirectory = backup,
                    FileCount = installedFiles.Length,
                    Bytes = bytes
                };
            }
            catch (Exception exception)
            {
                Logger.Exception("RUNTIME_SETUP", exception, "runtime setup failed; work=" + workRoot + "; staged=" + stagedRuntime);
                Report(progress, "失败：" + Logger.Redact(exception.Message));
                Report(progress, "为便于调试，临时目录已保留：" + workRoot);
                throw;
            }
            finally
            {
                if (success)
                {
                    TryDeleteDirectory(workRoot, progress);
                    TryDeleteDirectory(stagedRuntime, progress);
                }
            }
        }

        private void ValidateInputs(string installerPath, string sevenZipPath)
        {
            if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
                throw new FileNotFoundException("请选择已从移动云电脑官网下载的 Windows 安装包", installerPath);
            if (!string.Equals(Path.GetExtension(installerPath), ".exe", StringComparison.OrdinalIgnoreCase) ||
                new FileInfo(installerPath).Length < 100L * 1024 * 1024)
                throw new InvalidDataException("所选文件不像有效的移动云电脑 Windows 安装包");
            if (string.IsNullOrWhiteSpace(sevenZipPath) || !File.Exists(sevenZipPath))
                throw new FileNotFoundException("未检测到 7-Zip，请先下载并安装提取工具", sevenZipPath);
        }

        private static void EnsureFreeSpace(string path, long minimumBytes)
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path));
            DriveInfo drive = new DriveInfo(root);
            if (drive.AvailableFreeSpace < minimumBytes)
                throw new IOException("磁盘可用空间不足；" + root + " 至少需要 " + Math.Round(minimumBytes / 1073741824.0, 1) + " GiB");
        }

        private static void AddExistingEntries(ICollection<string> target, string root, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                string path = Path.Combine(root, names[i]);
                if (File.Exists(path)) target.Add(path);
            }
        }

        private static void CopyExistingFiles(string source, string target, string[] names, Action<string> progress, bool warnMissing)
        {
            for (int i = 0; i < names.Length; i++)
            {
                string from = Path.Combine(source, names[i]);
                if (File.Exists(from)) File.Copy(from, Path.Combine(target, names[i]), true);
                else if (warnMissing) Report(progress, "动态组件未找到，继续构建：" + names[i]);
            }
        }

        private static void CopyRelativeFile(string root, string file, string targetRoot)
        {
            string relative = PeDependencyResolver.Relative(root, file);
            string target = Path.Combine(targetRoot, relative);
            string directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.Copy(file, target, true);
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            string[] directories = Directory.GetDirectories(source, "*", SearchOption.AllDirectories);
            for (int i = 0; i < directories.Length; i++)
            {
                string relative = PeDependencyResolver.Relative(source, directories[i]);
                Directory.CreateDirectory(Path.Combine(target, relative));
            }
            string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                if (string.Equals(Path.GetExtension(files[i]), ".pdb", StringComparison.OrdinalIgnoreCase)) continue;
                string relative = PeDependencyResolver.Relative(source, files[i]);
                string destination = Path.Combine(target, relative);
                string directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.Copy(files[i], destination, true);
            }
        }

        private string InstallStagedRuntime(string stagedRuntime, string id, Action<string> progress)
        {
            string backup = string.Empty;
            if (Directory.Exists(_runtimeDirectory))
            {
                backup = _runtimeDirectory + ".backup-" + id;
                Directory.Move(_runtimeDirectory, backup);
                Report(progress, "原有不完整 runtime 已备份：" + backup);
            }
            try
            {
                Directory.Move(stagedRuntime, _runtimeDirectory);
                return backup;
            }
            catch
            {
                if (!string.IsNullOrEmpty(backup) && Directory.Exists(backup) && !Directory.Exists(_runtimeDirectory))
                    Directory.Move(backup, _runtimeDirectory);
                throw;
            }
        }

        private static void WriteManifest(string runtimeDirectory)
        {
            string[] files = Directory.GetFiles(runtimeDirectory, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            List<string> lines = new List<string>();
            using (SHA256 sha = SHA256.Create())
            {
                for (int i = 0; i < files.Length; i++)
                {
                    byte[] hash;
                    using (FileStream stream = File.OpenRead(files[i])) hash = sha.ComputeHash(stream);
                    lines.Add(ToHex(hash) + "\t" + new FileInfo(files[i]).Length + "\t" + PeDependencyResolver.Relative(runtimeDirectory, files[i]));
                }
            }
            File.WriteAllLines(Path.Combine(runtimeDirectory, "runtime-manifest.sha256"), lines.ToArray(), new UTF8Encoding(false));
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }

        private static void RunSevenZip(string sevenZipPath, string arguments, Action<string> progress)
        {
            Logger.Info("RUNTIME_SETUP", "7zip start exe=" + sevenZipPath + "; args=" + Logger.Redact(arguments));
            StringBuilder output = new StringBuilder();
            StringBuilder error = new StringBuilder();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (Process process = Process.Start(startInfo))
            {
                if (process == null) throw new InvalidOperationException("无法启动 7-Zip");
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (!string.IsNullOrEmpty(e.Data)) output.AppendLine(e.Data);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (!string.IsNullOrEmpty(e.Data)) error.AppendLine(e.Data);
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                process.WaitForExit();
                Logger.Info("RUNTIME_SETUP", "7zip exit_code=" + process.ExitCode + "; output_len=" + output.Length + "; error=" + Logger.Redact(error.ToString()));
                if (process.ExitCode != 0) throw new InvalidOperationException("7-Zip 提取失败，退出码 " + process.ExitCode + "：" + Logger.Redact(error.ToString()));
            }
            Report(progress, "7-Zip 提取完成");
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void Report(Action<string> progress, string message)
        {
            Logger.Info("RUNTIME_SETUP", message);
            if (progress != null) progress(message);
        }

        private static void TryDeleteDirectory(string path, Action<string> progress)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            try
            {
                Directory.Delete(path, true);
                Report(progress, "已清理临时目录：" + path);
            }
            catch (Exception exception)
            {
                Logger.Exception("RUNTIME_SETUP", exception, "temporary cleanup failed path=" + path);
                Report(progress, "临时目录清理失败，可稍后手动删除：" + path);
            }
        }
    }
}
