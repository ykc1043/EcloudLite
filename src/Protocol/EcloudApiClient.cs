using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using EcloudLite.Infrastructure;

namespace EcloudLite.Protocol
{
    internal sealed class EcloudApiClient
    {
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        private readonly Dictionary<string, object> _commonParams;
        private string _accessToken;

        public EcloudApiClient(string deviceUid)
        {
            _commonParams = BuildCommonParams(deviceUid);
        }

        public string DeviceUid { get { return Convert.ToString(_commonParams["deviceUid"]); } }
        public bool HasToken { get { return !string.IsNullOrEmpty(_accessToken); } }

        public void SetToken(string token)
        {
            if (!string.IsNullOrEmpty(token))
            {
                _accessToken = token.Trim();
                Logger.Info("AUTH", "access token installed; length=" + _accessToken.Length);
            }
        }

        public void ClearToken()
        {
            _accessToken = string.Empty;
            Logger.Info("AUTH", "access token cleared");
        }

        public Dictionary<string, object> Post(string endpoint, Dictionary<string, object> payload)
        {
            return JsonValue.AsDictionary(PostRaw(endpoint, payload));
        }

        public object PostRaw(string endpoint, Dictionary<string, object> payload)
        {
            string requestId = Guid.NewGuid().ToString("N").Substring(0, 12);
            Stopwatch timer = Stopwatch.StartNew();
            Dictionary<string, object> merged = new Dictionary<string, object>();
            if (payload != null)
            {
                foreach (KeyValuePair<string, object> pair in payload) merged[pair.Key] = pair.Value;
            }
            foreach (KeyValuePair<string, object> pair in _commonParams) merged[pair.Key] = pair.Value;
            if (!string.IsNullOrEmpty(_accessToken)) merged["accessToken"] = _accessToken;

            Logger.Info(
                "API",
                string.Format("request start id={0} endpoint={1} fields={2} token={3}",
                    requestId, endpoint, JsonValue.KeyList(merged), !string.IsNullOrEmpty(_accessToken)));

            try
            {
                string plainJson = _json.Serialize(merged);
                string encrypted = CryptoUtil.RsaEncrypt(plainJson);
                string requestJson = _json.Serialize(new Dictionary<string, object> { { "params", encrypted } });
                string responseText = Send(endpoint, requestJson, requestId);
                object body = DecodeResponse(responseText, endpoint, requestId);
                Dictionary<string, object> bodyDictionary = JsonValue.AsDictionary(body);
                timer.Stop();
                Logger.Info(
                    "API",
                    string.Format("request success id={0} endpoint={1} elapsed_ms={2} response_type={3} response_keys={4}",
                        requestId, endpoint, timer.ElapsedMilliseconds,
                        body == null ? "null" : body.GetType().Name,
                        bodyDictionary.Count == 0 ? "-" : JsonValue.KeyList(bodyDictionary)));
                return body;
            }
            catch (EcloudApiException ex)
            {
                timer.Stop();
                Logger.Warn(
                    "API",
                    string.Format("request business_error id={0} endpoint={1} elapsed_ms={2} code={3} message={4} response_keys={5}",
                        requestId, endpoint, timer.ElapsedMilliseconds, ex.ErrorCode, ex.Message,
                        JsonValue.KeyList(ex.ResponseObject)));
                throw;
            }
            catch (Exception ex)
            {
                timer.Stop();
                Logger.Exception(
                    "API",
                    ex,
                    string.Format("request failed id={0} endpoint={1} elapsed_ms={2}", requestId, endpoint, timer.ElapsedMilliseconds));
                throw;
            }
        }

        private string Send(string endpoint, string requestJson, string requestId)
        {
            string url = CryptoUtil.BuildSignedUrl(endpoint);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) EcloudCloudComputer/3.8.4 EcloudLite/0.1";
            request.Timeout = ProtocolConstants.LoginPaths.Contains(endpoint) ? 10000 : 30000;
            request.ReadWriteTimeout = request.Timeout;
            request.Proxy = null;
            byte[] bytes = Encoding.UTF8.GetBytes(requestJson);
            request.ContentLength = bytes.Length;
            Logger.Debug("HTTP", string.Format("send id={0} host={1} endpoint={2} bytes={3} timeout_ms={4} proxy=disabled", requestId, request.RequestUri.Host, endpoint, bytes.Length, request.Timeout));

            using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    string text = reader.ReadToEnd();
                    Logger.Debug("HTTP", string.Format("receive id={0} status={1} bytes={2}", requestId, (int)response.StatusCode, Encoding.UTF8.GetByteCount(text)));
                    return text;
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    using (response)
                    using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    {
                        string text = reader.ReadToEnd();
                        Logger.Warn("HTTP", string.Format("receive id={0} status={1} bytes={2}", requestId, (int)response.StatusCode, Encoding.UTF8.GetByteCount(text)));
                        if (!string.IsNullOrEmpty(text)) return text;
                    }
                }
                throw;
            }
        }

        private object DecodeResponse(string responseText, string endpoint, string requestId)
        {
            object envelopeObject = _json.DeserializeObject(responseText);
            Dictionary<string, object> envelope = JsonValue.AsDictionary(envelopeObject);
            object paramsValue;
            if (!envelope.TryGetValue("params", out paramsValue) || paramsValue == null)
            {
                throw BuildException(envelope, "响应缺少加密 params 字段");
            }

            string plain = CryptoUtil.RsaDecrypt(Convert.ToString(paramsValue));
            Dictionary<string, object> decoded = JsonValue.AsDictionary(_json.DeserializeObject(plain));
            string state = JsonValue.String(decoded, "state");
            string errorMessage = JsonValue.String(decoded, "errorMessage");
            if ((!string.IsNullOrEmpty(state) && !string.Equals(state, "OK", StringComparison.OrdinalIgnoreCase)) || !string.IsNullOrEmpty(errorMessage))
                throw BuildException(decoded, errorMessage);

            object bodyObject;
            if (!decoded.TryGetValue("body", out bodyObject)) bodyObject = decoded;
            Dictionary<string, object> body = JsonValue.AsDictionary(bodyObject);

            if (endpoint == ProtocolConstants.LoginVerifyAccessTicket)
            {
                string token = JsonValue.String(body, "accessToken");
                if (!string.IsNullOrEmpty(token)) SetToken(token);
            }
            Logger.Debug("API", string.Format("decrypt id={0} endpoint={1} decoded_keys={2} body_keys={3}", requestId, endpoint, JsonValue.KeyList(decoded), JsonValue.KeyList(body)));
            return bodyObject;
        }

        private static EcloudApiException BuildException(Dictionary<string, object> response, string fallback)
        {
            string code = FirstString(response, "errorCode", "code", "resultCode");
            string message = FirstString(response, "errorMessage", "message", "msg", "resultMessage");
            Dictionary<string, object> body = JsonValue.Dictionary(response, "body");
            if (string.IsNullOrEmpty(message) && body.Count > 0)
                message = FirstString(body, "errorMessage", "message", "msg", "resultMessage");
            if (string.IsNullOrEmpty(message)) message = fallback;
            return new EcloudApiException(code, message, response);
        }

        private static string FirstString(Dictionary<string, object> dictionary, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                string value = JsonValue.String(dictionary, keys[i]);
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return string.Empty;
        }

        private static Dictionary<string, object> BuildCommonParams(string deviceUid)
        {
            return new Dictionary<string, object>
            {
                { "companyCode", ProtocolConstants.CompanyCode },
                { "clientType", "pc_windows_64_yt" },
                { "clientVersion", ProtocolConstants.ClientVersion },
                { "deviceUid", deviceUid },
                { "deviceName", Environment.MachineName },
                { "deviceType", "pc" },
                { "operatingSystem", "Windows" },
                { "cores", Environment.ProcessorCount },
                { "ram", 8 },
                { "systemArchitecture", Environment.Is64BitOperatingSystem ? "x86_64" : "x86" },
                { "deviceCompany", "Generic" },
                { "deviceModel", "WindowsPC" },
                { "deviceSystem", "Windows" },
                { "operatingVersion", Environment.OSVersion.VersionString },
                { "processor", "Unknown" },
                { "diskTotal", 500.0 },
                { "diskUsed", 250.0 },
                { "ipAddress", "127.0.0.1" },
                { "macAddress", "00:00:00:00:00:00" }
            };
        }
    }
}
