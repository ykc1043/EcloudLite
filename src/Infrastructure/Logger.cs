using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EcloudLite.Infrastructure
{
    internal enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }

    internal sealed class LogEntry
    {
        public DateTime TimestampUtc { get; set; }
        public LogLevel Level { get; set; }
        public string Category { get; set; }
        public string Message { get; set; }
        public int ThreadId { get; set; }

        public override string ToString()
        {
            return string.Format(
                "{0:yyyy-MM-ddTHH:mm:ss.fffZ} [{1,-5}] [{2}] [T{3}] {4}",
                TimestampUtc,
                Level.ToString().ToUpperInvariant(),
                Category,
                ThreadId,
                Message);
        }
    }

    internal static class Logger
    {
        private static readonly object Gate = new object();
        private static string _currentFile;
        private static bool _fileFailureReported;

        public static event Action<LogEntry> EntryWritten;

        public static string CurrentFile { get { return _currentFile; } }

        public static void Initialize()
        {
            try
            {
                AppPaths.EnsureCreated();
                _currentFile = Path.Combine(
                    AppPaths.Logs,
                    "ecloud-lite-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                Info("BOOT", "logger initialized path=" + _currentFile);
            }
            catch (Exception exception)
            {
                _currentFile = null;
                EmergencyWrite("logger initialization failed", exception);
            }
        }

        public static void Debug(string category, string message) { Write(LogLevel.Debug, category, message); }
        public static void Info(string category, string message) { Write(LogLevel.Info, category, message); }
        public static void Warn(string category, string message) { Write(LogLevel.Warn, category, message); }
        public static void Error(string category, string message) { Write(LogLevel.Error, category, message); }

        public static void Exception(string category, Exception exception, string context)
        {
            string message = string.Format(
                "{0}; exception={1}; message={2}",
                context,
                exception.GetType().FullName,
                Redact(exception.Message));
            Error(category, message);
            Debug(category, Redact(exception.StackTrace ?? "<no stack>"));
        }

        public static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value ?? string.Empty;

            string result = value;
            result = Regex.Replace(
                result,
                @"(?i)(password|passwd|accessToken|accessTicket|verificationCode|params|connectStr|secret|ticket)\s*[:=]\s*[^,;\s}]+",
                "$1=<redacted>");
            result = Regex.Replace(
                result,
                @"(?<!\d)(1\d{2})\d{4}(\d{4})(?!\d)",
                "$1****$2");
            result = Regex.Replace(
                result,
                @"[A-Za-z0-9_\-+/=:.]{48,}",
                delegate(Match match)
                {
                    return "<redacted:" + match.Value.Length + ">";
                });
            return result;
        }

        public static string MaskAccount(string value)
        {
            if (string.IsNullOrEmpty(value)) return "<empty>";
            if (value.Length <= 3) return new string('*', value.Length);
            return value.Substring(0, 2) + new string('*', Math.Max(2, value.Length - 4)) + value.Substring(value.Length - 2);
        }

        public static string ShortId(string value)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            return value.Length <= 12 ? value : value.Substring(0, 8) + "..." + value.Substring(value.Length - 4);
        }

        private static void Write(LogLevel level, string category, string message)
        {
            LogEntry entry = new LogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Level = level,
                Category = string.IsNullOrEmpty(category) ? "APP" : category,
                Message = Redact(message ?? string.Empty),
                ThreadId = Thread.CurrentThread.ManagedThreadId
            };

            string line = entry + Environment.NewLine;
            lock (Gate)
            {
                if (!string.IsNullOrEmpty(_currentFile))
                {
                    try
                    {
                        File.AppendAllText(_currentFile, line, new UTF8Encoding(false));
                    }
                    catch (Exception exception)
                    {
                        _currentFile = null;
                        if (!_fileFailureReported)
                        {
                            _fileFailureReported = true;
                            EmergencyWrite("log file write failed; file logging disabled", exception);
                        }
                    }
                }
            }

            Action<LogEntry> handler = EntryWritten;
            if (handler != null)
            {
                try { handler(entry); }
                catch { }
            }
        }

        private static void EmergencyWrite(string context, Exception exception)
        {
            try
            {
                Console.Error.WriteLine(
                    "{0:O} [ERROR] [LOGGER] {1}; exception={2}; message={3}",
                    DateTime.UtcNow,
                    context,
                    exception == null ? "<none>" : exception.GetType().FullName,
                    exception == null ? "<none>" : Redact(exception.Message));
            }
            catch
            {
                // Logging failures must never terminate the application.
            }
        }
    }
}
