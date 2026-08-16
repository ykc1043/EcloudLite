using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using EcloudLite.Infrastructure;
using EcloudLite.Protocol;

namespace EcloudLite.Services
{
    internal enum AuthChallengeType
    {
        None,
        DeviceTrust,
        TwoFactor,
        EnhancedSms,
        FourA
    }

    internal sealed class LoginResult
    {
        public bool Success { get; set; }
        public string AccessToken { get; set; }
        public AuthChallengeType Challenge { get; set; }
        public string Mobile { get; set; }
        public string LoginCode { get; set; }
        public string ErrorCode { get; set; }
        public string Message { get; set; }
    }

    internal sealed class LoginService
    {
        private const string UntrustedDevice = "30002009";
        private const string TwoFactor = "30002060";
        private const string EnhancedStrategy = "30002063";

        private readonly EcloudApiClient _client;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public LoginService(EcloudApiClient client)
        {
            _client = client;
        }

        public LoginResult LoginWithPassword(string username, string password)
        {
            Logger.Info("AUTH", "password login start account=" + Logger.MaskAccount(username));
            try
            {
                Dictionary<string, object> response = _client.Post(
                    ProtocolConstants.LoginVerify,
                    new Dictionary<string, object>
                    {
                        { "username", username },
                        { "password", password },
                        { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                        { "clientNeedTwoFactor", true }
                    });

                string ticket = JsonValue.String(response, "accessTicket");
                if (!string.IsNullOrEmpty(ticket)) return ExchangeTicket(ticket);

                string token = JsonValue.String(response, "accessToken");
                if (!string.IsNullOrEmpty(token))
                {
                    _client.SetToken(token);
                    return Success(token);
                }

                string responseCode = JsonValue.String(response, "errorCode");
                if (!string.IsNullOrEmpty(responseCode)) return Classify(responseCode, response, "登录需要额外验证");
                return Failure(string.Empty, "登录响应未包含 accessTicket 或 accessToken");
            }
            catch (EcloudApiException ex)
            {
                return Classify(ex.ErrorCode, ex.ResponseObject, ex.Message);
            }
        }

        public void SendStandaloneSmsCode(string loginName)
        {
            if (string.IsNullOrWhiteSpace(loginName))
                throw new InvalidOperationException("请输入账号或手机号");
            Logger.Info("AUTH", "standalone sms send requested account=" + Logger.MaskAccount(loginName));
            _client.Post(ProtocolConstants.LoginSendSms, BuildStandaloneSmsRequest(loginName));
            Logger.Info("AUTH", "standalone sms send request completed");
        }

        public LoginResult LoginWithSms(string loginName, string verificationCode)
        {
            string normalizedCode = NormalizeCode(verificationCode);
            if (string.IsNullOrWhiteSpace(loginName)) return Failure(string.Empty, "请输入账号或手机号");
            if (string.IsNullOrEmpty(normalizedCode)) return Failure(string.Empty, "请输入短信验证码");
            Logger.Info("AUTH", "standalone sms login start account=" + Logger.MaskAccount(loginName) +
                " code_length=" + normalizedCode.Length);
            try
            {
                Dictionary<string, object> response = _client.Post(
                    ProtocolConstants.LoginVerifySms,
                    BuildStandaloneSmsLoginRequest(loginName, normalizedCode));

                string ticket = JsonValue.String(response, "accessTicket");
                if (!string.IsNullOrEmpty(ticket)) return ExchangeTicket(ticket);

                string token = JsonValue.String(response, "accessToken");
                if (!string.IsNullOrEmpty(token))
                {
                    _client.SetToken(token);
                    return Success(token);
                }

                string responseCode = JsonValue.String(response, "errorCode");
                if (!string.IsNullOrEmpty(responseCode)) return Classify(responseCode, response, "短信登录需要额外验证");
                return Failure(string.Empty, "短信登录响应未包含 accessTicket 或 accessToken");
            }
            catch (EcloudApiException ex)
            {
                return Classify(ex.ErrorCode, ex.ResponseObject, ex.Message);
            }
        }

        internal static Dictionary<string, object> BuildStandaloneSmsRequest(string loginName)
        {
            return new Dictionary<string, object>
            {
                { "mobile", loginName },
                { "codeType", "login" }
            };
        }

        internal static Dictionary<string, object> BuildStandaloneSmsLoginRequest(string loginName, string verificationCode)
        {
            return new Dictionary<string, object>
            {
                { "mobile", loginName },
                { "verificationCode", verificationCode },
                { "isNeedTemporaryDeviceSelection", true }
            };
        }

        public void SendChallengeCode(AuthChallengeType challenge, string mobile, string username)
        {
            Logger.Info("AUTH", string.Format("sms send requested challenge={0} mobile={1}", challenge, Logger.Redact(mobile)));
            if (challenge == AuthChallengeType.DeviceTrust)
            {
                _client.Post(ProtocolConstants.LoginSendSms, new Dictionary<string, object>
                {
                    { "mobile", mobile },
                    { "codeType", "trust" }
                });
            }
            else if (challenge == AuthChallengeType.TwoFactor)
            {
                _client.Post(ProtocolConstants.LoginTwoFactorSend, new Dictionary<string, object>
                {
                    { "mobile", mobile },
                    { "userName", username }
                });
            }
            else if (challenge == AuthChallengeType.EnhancedSms)
            {
                _client.Post(ProtocolConstants.LoginSendSms, new Dictionary<string, object>
                {
                    { "mobile", mobile },
                    { "codeType", "login" }
                });
            }
            else
            {
                throw new InvalidOperationException("当前登录状态不支持发送短信");
            }
            Logger.Info("AUTH", "sms send request completed challenge=" + challenge);
        }

        public LoginResult CompleteChallenge(
            AuthChallengeType challenge,
            string mobile,
            string username,
            string password,
            string verificationCode,
            string loginCode)
        {
            string normalizedCode = NormalizeCode(verificationCode);
            if (string.IsNullOrEmpty(normalizedCode)) return Failure(string.Empty, "请输入短信验证码");
            Logger.Info("AUTH", string.Format("challenge verify start type={0} code_length={1}", challenge, normalizedCode.Length));

            Dictionary<string, object> response;
            if (challenge == AuthChallengeType.DeviceTrust)
            {
                Dictionary<string, object> payload = new Dictionary<string, object>
                {
                    { "mobile", mobile },
                    { "verificationCode", normalizedCode },
                    { "isNeedTemporaryDeviceSelection", true },
                    { "code", string.IsNullOrEmpty(loginCode) ? null : (object)loginCode },
                    { "loginUserName", username }
                };
                response = _client.Post(ProtocolConstants.LoginTrustDevice, payload);
                string ticket = JsonValue.String(response, "accessTicket");
                if (string.IsNullOrEmpty(ticket)) return Failure(JsonValue.String(response, "errorCode"), "设备信任响应缺少 accessTicket");
                _client.Post(ProtocolConstants.LoginTemporaryDevice, new Dictionary<string, object>
                {
                    { "accessTicket", ticket },
                    { "isTemporary", 0 }
                });
                return ExchangeTicket(ticket);
            }

            if (challenge == AuthChallengeType.TwoFactor)
            {
                Dictionary<string, object> payload = new Dictionary<string, object>
                {
                    { "mobile", mobile },
                    { "userName", username },
                    { "verificationCode", normalizedCode },
                    { "password", password }
                };
                if (!string.IsNullOrEmpty(loginCode)) payload["code"] = loginCode;
                response = _client.Post(ProtocolConstants.LoginTwoFactorVerify, payload);
                return ExchangeResponseTicket(response, "二次验证响应缺少 accessTicket");
            }

            if (challenge == AuthChallengeType.EnhancedSms)
            {
                Dictionary<string, object> payload = new Dictionary<string, object>
                {
                    { "mobile", mobile },
                    { "userName", username },
                    { "verificationCode", normalizedCode }
                };
                if (!string.IsNullOrEmpty(loginCode)) payload["code"] = loginCode;
                response = _client.Post(ProtocolConstants.LoginEnhancedVerify, payload);
                return ExchangeResponseTicket(response, "增强验证响应缺少 accessTicket");
            }

            return Failure(string.Empty, "当前登录状态不支持验证码提交");
        }

        public Dictionary<string, object> GetUserInfo()
        {
            return _client.Post(ProtocolConstants.GetLoginUserInfo, new Dictionary<string, object>());
        }

        public void Logout()
        {
            try { _client.Post(ProtocolConstants.Logout, new Dictionary<string, object>()); }
            catch (Exception ex) { Logger.Exception("AUTH", ex, "server logout failed; clearing local token"); }
            _client.ClearToken();
        }

        private LoginResult ExchangeResponseTicket(Dictionary<string, object> response, string missingMessage)
        {
            string ticket = JsonValue.String(response, "accessTicket");
            if (string.IsNullOrEmpty(ticket)) return Failure(JsonValue.String(response, "errorCode"), missingMessage);
            return ExchangeTicket(ticket);
        }

        private LoginResult ExchangeTicket(string ticket)
        {
            Dictionary<string, object> response = _client.Post(
                ProtocolConstants.LoginVerifyAccessTicket,
                new Dictionary<string, object> { { "accessTicket", ticket } });
            string token = JsonValue.String(response, "accessToken");
            if (string.IsNullOrEmpty(token)) return Failure(string.Empty, "Token 交换响应缺少 accessToken");
            _client.SetToken(token);
            Logger.Info("AUTH", "login completed; token_length=" + token.Length);
            return Success(token);
        }

        private LoginResult Classify(string code, Dictionary<string, object> response, string message)
        {
            Dictionary<string, object> body = BodyDictionary(response);
            string mobile = JsonValue.String(body, "mobile");
            string loginCode = JsonValue.String(body, "code");
            AuthChallengeType challenge = AuthChallengeType.None;
            if (code == UntrustedDevice) challenge = AuthChallengeType.DeviceTrust;
            else if (code == TwoFactor) challenge = AuthChallengeType.TwoFactor;
            else if (code == EnhancedStrategy) challenge = AuthChallengeType.EnhancedSms;
            else if (!string.IsNullOrEmpty(JsonValue.String(response, "userId"))) challenge = AuthChallengeType.FourA;

            if (challenge != AuthChallengeType.None)
            {
                Logger.Warn("AUTH", string.Format("login challenge type={0} code={1} mobile={2} login_code_present={3}", challenge, code, Logger.Redact(mobile), !string.IsNullOrEmpty(loginCode)));
                return new LoginResult
                {
                    Success = false,
                    Challenge = challenge,
                    Mobile = mobile,
                    LoginCode = loginCode,
                    ErrorCode = code,
                    Message = message
                };
            }
            return Failure(code, message);
        }

        private Dictionary<string, object> BodyDictionary(Dictionary<string, object> response)
        {
            object body;
            if (response != null && response.TryGetValue("body", out body))
            {
                Dictionary<string, object> dictionary = JsonValue.AsDictionary(body);
                if (dictionary.Count > 0) return dictionary;
                string bodyText = body as string;
                if (!string.IsNullOrEmpty(bodyText))
                {
                    try { return JsonValue.AsDictionary(_json.DeserializeObject(bodyText)); }
                    catch { }
                }
            }
            return new Dictionary<string, object>();
        }

        private static LoginResult Success(string token)
        {
            return new LoginResult { Success = true, AccessToken = token, Challenge = AuthChallengeType.None, Message = "登录成功" };
        }

        private static LoginResult Failure(string code, string message)
        {
            Logger.Warn("AUTH", string.Format("login failed code={0} message={1}", code, message));
            return new LoginResult { Success = false, ErrorCode = code, Message = message, Challenge = AuthChallengeType.None };
        }

        private static string NormalizeCode(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string full = "０１２３４５６７８９";
            string half = "0123456789";
            char[] input = value.Trim().ToCharArray();
            System.Text.StringBuilder output = new System.Text.StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                if (char.IsWhiteSpace(input[i])) continue;
                int index = full.IndexOf(input[i]);
                output.Append(index >= 0 ? half[index] : input[i]);
            }
            return output.ToString();
        }
    }
}
