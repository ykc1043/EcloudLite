using System.Collections.Generic;

namespace EcloudLite.Protocol
{
    internal static class ProtocolConstants
    {
        public const string BaseUrl = "https://cloudpc.ecloud.10086.cn";
        public const string ApiPath = "/api/cem/gateway/outer/cem-webapi";
        public const string AccessKey = "53bb79015a3f47c4be166d9371f68f14";
        public const string SecretKey = "6b0d3b93f3aa4c7ea076c841bead1ddd";
        public const string SignMethod = "HmacSHA1";
        public const string SignVersion = "V2.0";
        public const string HmacPrefix = "BC_SIGNATURE&";
        public const string ClientVersion = "3.8.4";
        public const string ChannelVersion = "23";
        public const string CompanyCode = "ECloud";
        public const string CsapId = "3fec8a54-7e49-48";

        public const string LoginVerify = "/login/verify";
        public const string LoginVerifyAccessTicket = "/login/verifyAccessTicket";
        public const string LoginSendSms = "/login/sendVerifySms";
        public const string LoginVerifySms = "/login/verifySms";
        public const string LoginTrustDevice = "/login/trustDevice";
        public const string LoginTemporaryDevice = "/login/trustOrTemporaryDevice";
        public const string LoginTwoFactorSend = "/login/special/getSecondauthSms";
        public const string LoginTwoFactorVerify = "/login/verifyTwoFactorAuthSms";
        public const string LoginEnhancedVerify = "/login/verifyLoginEnhanceSms";
        public const string Logout = "/login/logout";
        public const string GetLoginUserInfo = "/user/getLoginUserInfo";
        public const string GetDeviceInfo = "/user/getDeviceInfo";
        public const string GetDesktopStatus = "/user/getDesktopStatus";
        public const string ResourceOperate = "/resource/operate";
        public const string DesktopUptime = "/resource/desktopUptime";

        public static readonly HashSet<string> LoginPaths = new HashSet<string>
        {
            LoginVerify,
            LoginVerifySms
        };

        public const string PublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQCqisJL7YvdPC/gJA7fLrr1G+t6
J0arJr0sVfieVJTXTclm/2afP/fjNYY/CFcg1MUx8KPmPC2CqsUHRMZq6Ev1/UNX
E74I1TfJC/2b8aexcdZ+Lokj7AwzrM9yPy2qfV6vXtxyRrTs+JcFHVXtV6phNkor
NyIahyfy46+iNB+FSQIDAQAB
-----END PUBLIC KEY-----";

        public const string PrivateKeyPem = @"-----BEGIN PRIVATE KEY-----
MIICdQIBADANBgkqhkiG9w0BAQEFAASCAl8wggJbAgEAAoGBAKqKwkvti908L+Ak
Dt8uuvUb63onRqsmvSxV+J5UlNdNyWb/Zp8/9+M1hj8IVyDUxTHwo+Y8LYKqxQdE
xmroS/X9Q1cTvgjVN8kL/Zvxp7Fx1n4uiSPsDDOsz3I/Lap9Xq9e3HJGtOz4lwUd
Ve1XqmE2Sis3IhqHJ/Ljr6I0H4VJAgMBAAECgYBD6lx0BlajtRtPxKxTfvWfNQ4y
qD+BWz0M0fPfgcmAcI7bQKyqkLv0NNWQdo7UGUeqmq16u85X8g/i1CW8X2QYHOSY
NBUWsK3k5gFT1wdk+bwuIMZqgjEc48TXzM4pidcplJLyD1tnNiubzcXIsZCIIuQ/
GmWcuxn7ULHnXDsQMQJBANMl4V97be6fkd1beGqYZWIx3XNnL96AQsapBrEbbORT
u/JnwTCRbsRWRBHU11FZuK85dBDXrH8reoAsgepmsF0CQQDOxL99OFjozj8g1weF
GwI/otMKcPhkaslU2tj3QF44zT1TZiOZ710I8GQLPlKeu1yGWvVUwgH4bCY0M8M1
/gndAkB9sU4RTeOqKjllwT7UjbXEl5SRTzrSxB18L0B5i67t2N7INXVumRSMMiJB
TyeCGNv1C0mJgSoBZft9c4E+7TRNAkB+7Azza7Q/6+KaYQRPs32U3HkZbrE6ysYd
XV1ToOJ1kZ60Y/00j9cXFqECudXzc+Ve39S6m4CkIpbs8l1A9ljNAkBy6Rp19R5w
WMr/3feIMZ18akWXT5mgRvZpkT5MgmrjVu1lRv8bHsEsAzRYvdPSjzp0nCkUbOWU
ITxWp7d//Fwc
-----END PRIVATE KEY-----";
    }
}
