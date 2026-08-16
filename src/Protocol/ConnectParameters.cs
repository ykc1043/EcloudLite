using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EcloudLite.Protocol
{
    internal sealed class ConnectParameters
    {
        public string Hv6 { get; set; }
        public string Key { get; set; }
        public string Vmid { get; set; }
        public int Port { get; set; }
        public int ProxySport { get; set; }
        public string[] FlagNames { get; set; }

        public bool IsComplete
        {
            get { return !string.IsNullOrEmpty(Hv6) && !string.IsNullOrEmpty(Key) && !string.IsNullOrEmpty(Vmid); }
        }
    }

    internal static class ConnectStringParser
    {
        public static ConnectParameters Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("连接串为空", "text");
            List<string> tokens = Tokenize(text);
            Dictionary<string, string> flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];
                string key = null;
                if (token.StartsWith("--", StringComparison.Ordinal) && token.Length > 2)
                    key = token.Substring(2);
                else if (token.StartsWith("-", StringComparison.Ordinal) && token.Length == 2)
                    key = token.Substring(1);
                if (key == null) continue;

                string value = "true";
                if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith("-", StringComparison.Ordinal))
                    value = tokens[++i];
                flags[key] = value;
            }

            string vmid = Value(flags, "vmid", "vm-id");
            if (string.IsNullOrEmpty(vmid))
            {
                Match match = Regex.Match(text, @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
                if (match.Success) vmid = match.Value;
            }

            return new ConnectParameters
            {
                Hv6 = Value(flags, "hv6", "h"),
                Key = Value(flags, "k"),
                Vmid = vmid,
                Port = ToInt(Value(flags, "p", "pv6"), 5100),
                ProxySport = ToInt(Value(flags, "proxy-sport", "sport"), 60063),
                FlagNames = flags.Keys.OrderBy(delegate(string value) { return value; }, StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }

        private static string Value(Dictionary<string, string> flags, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                string value;
                if (flags.TryGetValue(names[i], out value) && !string.IsNullOrEmpty(value)) return value;
            }
            return string.Empty;
        }

        private static int ToInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, out parsed) && parsed > 0 && parsed <= 65535 ? parsed : fallback;
        }

        private static List<string> Tokenize(string text)
        {
            List<string> tokens = new List<string>();
            MatchCollection matches = Regex.Matches(text, @"(?:""([^""]*)""|'([^']*)'|([^\s]+))");
            foreach (Match match in matches)
            {
                tokens.Add(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value);
            }
            return tokens;
        }
    }
}
