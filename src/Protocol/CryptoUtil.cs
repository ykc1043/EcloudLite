using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EcloudLite.Protocol
{
    internal static class CryptoUtil
    {
        private static readonly object KeyGate = new object();
        private static RSAParameters? _publicParameters;
        private static RSAParameters? _privateParameters;

        public static string RsaEncrypt(string plaintext)
        {
            byte[] data = Encoding.UTF8.GetBytes(plaintext);
            using (RSACryptoServiceProvider rsa = NewPublicRsa())
            using (MemoryStream output = new MemoryStream())
            {
                const int chunkSize = 117;
                for (int offset = 0; offset < data.Length; offset += chunkSize)
                {
                    int length = Math.Min(chunkSize, data.Length - offset);
                    byte[] chunk = new byte[length];
                    Buffer.BlockCopy(data, offset, chunk, 0, length);
                    byte[] encrypted = rsa.Encrypt(chunk, false);
                    output.Write(encrypted, 0, encrypted.Length);
                }
                return Convert.ToBase64String(output.ToArray());
            }
        }

        public static string RsaDecrypt(string ciphertextBase64)
        {
            byte[] data = Convert.FromBase64String(ciphertextBase64);
            if (data.Length % 128 != 0)
                throw new CryptographicException("RSA response length is not a multiple of 128 bytes");

            using (RSACryptoServiceProvider rsa = NewPrivateRsa())
            using (MemoryStream output = new MemoryStream())
            {
                for (int offset = 0; offset < data.Length; offset += 128)
                {
                    byte[] chunk = new byte[128];
                    Buffer.BlockCopy(data, offset, chunk, 0, 128);
                    byte[] decrypted = rsa.Decrypt(chunk, false);
                    output.Write(decrypted, 0, decrypted.Length);
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        public static string BuildSignedUrl(string endpoint)
        {
            return BuildSignedUrl(endpoint, DateTime.UtcNow.AddHours(8), Guid.NewGuid().ToString("N"));
        }

        internal static string BuildSignedUrl(string endpoint, DateTime utc8Time, string nonce)
        {
            string timestamp = utc8Time.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            string canonical =
                "AccessKey=" + Encode(ProtocolConstants.AccessKey) +
                "&SignatureMethod=" + Encode(ProtocolConstants.SignMethod) +
                "&SignatureNonce=" + Encode(nonce) +
                "&SignatureVersion=" + Encode(ProtocolConstants.SignVersion) +
                "&Timestamp=" + Encode(timestamp);

            string encodedPath = Encode(ProtocolConstants.ApiPath + endpoint);
            string stringToSign = "POST\n" + encodedPath + "\n" + Sha256Hex(canonical);
            string signature = HmacSha1Hex(stringToSign, ProtocolConstants.HmacPrefix + ProtocolConstants.SecretKey);

            return ProtocolConstants.BaseUrl + ProtocolConstants.ApiPath + endpoint + "?" + canonical + "&Signature=" + signature;
        }

        public static string Sha256Hex(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
            }
        }

        private static string HmacSha1Hex(string value, string key)
        {
            using (HMACSHA1 hmac = new HMACSHA1(Encoding.UTF8.GetBytes(key)))
            {
                return ToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
            }
        }

        private static string Encode(string value)
        {
            return Uri.EscapeDataString(value);
        }

        private static string ToHex(byte[] data)
        {
            StringBuilder builder = new StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++) builder.Append(data[i].ToString("x2"));
            return builder.ToString();
        }

        private static RSACryptoServiceProvider NewPublicRsa()
        {
            EnsureKeys();
            RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(1024);
            rsa.PersistKeyInCsp = false;
            rsa.ImportParameters(_publicParameters.Value);
            return rsa;
        }

        private static RSACryptoServiceProvider NewPrivateRsa()
        {
            EnsureKeys();
            RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(1024);
            rsa.PersistKeyInCsp = false;
            rsa.ImportParameters(_privateParameters.Value);
            return rsa;
        }

        private static void EnsureKeys()
        {
            if (_publicParameters.HasValue && _privateParameters.HasValue) return;
            lock (KeyGate)
            {
                if (!_publicParameters.HasValue)
                    _publicParameters = PemKeyParser.ReadPublicKey(ProtocolConstants.PublicKeyPem);
                if (!_privateParameters.HasValue)
                    _privateParameters = PemKeyParser.ReadPrivateKey(ProtocolConstants.PrivateKeyPem);
            }
        }
    }
}
