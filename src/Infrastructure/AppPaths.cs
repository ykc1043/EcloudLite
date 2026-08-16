using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace EcloudLite.Infrastructure
{
    internal static class AppPaths
    {
        private static readonly object Gate = new object();
        private static string _rootPath;

        public static string Root
        {
            get
            {
                EnsureCreated();
                return _rootPath;
            }
        }

        public static string Logs { get { return Path.Combine(Root, "logs"); } }
        public static string Settings { get { return Path.Combine(Root, "settings.json"); } }

        public static void EnsureCreated()
        {
            if (!string.IsNullOrEmpty(_rootPath)) return;

            lock (Gate)
            {
                if (!string.IsNullOrEmpty(_rootPath)) return;

                List<string> candidates = new List<string>();
                AddCandidate(candidates, Environment.GetEnvironmentVariable("ECLOUDLITE_DATA_DIR"));
                AddCandidate(candidates, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"));
                AddCandidate(candidates, Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EcloudLite"));
                AddCandidate(candidates, Path.Combine(Path.GetTempPath(), "EcloudLite"));

                Exception lastError = null;
                foreach (string candidate in candidates)
                {
                    try
                    {
                        Directory.CreateDirectory(candidate);
                        Directory.CreateDirectory(Path.Combine(candidate, "logs"));
                        _rootPath = candidate;
                        return;
                    }
                    catch (Exception exception)
                    {
                        if (!IsPathFailure(exception)) throw;
                        lastError = exception;
                    }
                }

                throw new IOException(
                    "No writable EcloudLite data directory is available",
                    lastError);
            }
        }

        private static void AddCandidate(ICollection<string> candidates, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string fullPath = Path.GetFullPath(path);
            foreach (string existing in candidates)
            {
                if (string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase)) return;
            }
            candidates.Add(fullPath);
        }

        private static bool IsPathFailure(Exception exception)
        {
            return exception is UnauthorizedAccessException ||
                   exception is IOException ||
                   exception is SecurityException ||
                   exception is NotSupportedException;
        }
    }
}
