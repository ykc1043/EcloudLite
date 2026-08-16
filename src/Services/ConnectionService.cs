using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using EcloudLite.Infrastructure;
using EcloudLite.Models;
using EcloudLite.Protocol;

namespace EcloudLite.Services
{
    internal sealed class ConnectResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int CagPort { get; set; }
        public int ConnectStringHexLength { get; set; }
        public int PlainLength { get; set; }
        public string PlainSha16 { get; set; }
        public bool HasKey { get; set; }
        public bool HasHv6 { get; set; }
        public string CagHost { get; set; }
        public ConnectParameters Parameters { get; set; }
    }

    internal sealed class ConnectionService
    {
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public ConnectResult RequestConnectInfo(Desktop desktop)
        {
            if (desktop == null) throw new ArgumentNullException("desktop");
            ConnectTarget target = ResolveTarget(desktop);
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            string requestPlain = CsapCrypto.BuildRequestJson(desktop.MachineId, 3, timestamp);
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "encrypt", 7 },
                { "language", "zh" },
                { "param", CsapCrypto.EncryptRequest(requestPlain) },
                { "timestamp", timestamp }
            };

            Logger.Info("CONNECT", string.Format(
                "connect info start instance={0} machine={1} cag_port={2} csapip_present={3} op_type=3",
                Logger.ShortId(desktop.InstanceId), Logger.ShortId(desktop.MachineId), target.Port, !string.IsNullOrEmpty(target.CsapIp)));

            string responseText = Post(target, _json.Serialize(body));
            Dictionary<string, object> response = JsonValue.AsDictionary(_json.DeserializeObject(responseText));
            Logger.Debug("CONNECT", "connect info response status=200 keys=" + JsonValue.KeyList(response));

            string connectHex = JsonValue.String(response, "connectStr");
            if (string.IsNullOrEmpty(connectHex)) connectHex = JsonValue.String(response, "connectstr");
            if (string.IsNullOrEmpty(connectHex))
                throw new InvalidOperationException("CAG 响应未返回 connectStr，result=" + JsonValue.String(response, "result"));

            string plain = CsapCrypto.DecodeConnectString(connectHex);
            if (string.IsNullOrEmpty(plain)) throw new InvalidOperationException("connectStr 解密结果为空");
            ConnectParameters parameters = ConnectStringParser.Parse(plain);
            bool hasKey = !string.IsNullOrEmpty(parameters.Key);
            bool hasHv6 = !string.IsNullOrEmpty(parameters.Hv6);
            string digest = CryptoUtil.Sha256Hex(plain).Substring(0, 16);

            Logger.Info("CONNECT", string.Format(
                "connect info success instance={0} cag_port={1} hex_len={2} plain_len={3} plain_sha16={4} has_k={5} has_hv6={6}",
                Logger.ShortId(desktop.InstanceId), target.Port, connectHex.Length, plain.Length, digest, hasKey, hasHv6));
            Logger.Debug("CONNECT", string.Format(
                "connect flags names={0} key_len={1} hv6_len={2} vmid_matches_machine={3} port={4} proxy_sport={5}",
                string.Join(",", parameters.FlagNames ?? new string[0]),
                parameters.Key == null ? 0 : parameters.Key.Length,
                parameters.Hv6 == null ? 0 : parameters.Hv6.Length,
                string.Equals(parameters.Vmid, desktop.MachineId, StringComparison.OrdinalIgnoreCase),
                parameters.Port,
                parameters.ProxySport));

            return new ConnectResult
            {
                Success = true,
                Message = "连接参数已获取，可进入串流会话",
                CagPort = target.Port,
                ConnectStringHexLength = connectHex.Length,
                PlainLength = plain.Length,
                PlainSha16 = digest,
                HasKey = hasKey,
                HasHv6 = hasHv6,
                CagHost = target.Host,
                Parameters = parameters
            };
        }

        private string Post(ConnectTarget target, string requestJson)
        {
            string url = "http://" + target.Host + ":" + target.Port + "/cs/cs_suOperDesktop.action";
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/xml";
            request.Accept = "*/*";
            request.Headers["X-Ap-sHost"] = target.CsapIp;
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            request.Proxy = null;
            byte[] bytes = Encoding.UTF8.GetBytes(requestJson);
            request.ContentLength = bytes.Length;
            using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                string text = reader.ReadToEnd();
                if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                    throw new InvalidOperationException("CAG HTTP status=" + (int)response.StatusCode);
                return text;
            }
        }

        private static ConnectTarget ResolveTarget(Desktop desktop)
        {
            Dictionary<string, object> custom = JsonValue.AsDictionary(desktop.CustomLoginParams);
            string csapIp = JsonValue.String(custom, "csapip");
            if (string.IsNullOrEmpty(csapIp)) csapIp = JsonValue.String(custom, "csapipv6");
            List<ConnectTarget> candidates = new List<ConnectTarget>();
            foreach (object item in JsonValue.Array(custom, "cagList"))
            {
                Dictionary<string, object> cag = JsonValue.AsDictionary(item);
                string address = JsonValue.String(cag, "addr");
                int port = ToInt(cag, "port", 8899);
                if (!string.IsNullOrEmpty(address)) candidates.Add(new ConnectTarget { Host = address, Port = port, CsapIp = csapIp });
            }
            if (candidates.Count == 0) throw new InvalidOperationException("桌面未返回 CAG 地址列表");

            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i].Port == 8899) return candidates[i];
            return candidates[0];
        }

        private static int ToInt(Dictionary<string, object> dictionary, string key, int fallback)
        {
            int value;
            return int.TryParse(JsonValue.String(dictionary, key), out value) ? value : fallback;
        }

        private sealed class ConnectTarget
        {
            public string Host { get; set; }
            public int Port { get; set; }
            public string CsapIp { get; set; }
        }
    }
}
