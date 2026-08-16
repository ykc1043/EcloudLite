using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EcloudLite.Protocol
{
    internal static class CsapCrypto
    {
        private static readonly byte[] RequestKey = BuildRequestKey();

        public static string BuildRequestJson(string vmid, int opType, string timestamp)
        {
            return "{\n" +
                "\t\"opType\" : " + opType.ToString(CultureInfo.InvariantCulture) + ",\n" +
                "\t\"timestamp\" : \"" + timestamp + "\",\n" +
                "\t\"vmid\" : \"" + vmid + "\"\n" +
                "}\n";
        }

        public static string EncryptRequest(string plaintext)
        {
            return EncryptBase64(Encoding.UTF8.GetBytes(plaintext), RequestKey);
        }

        public static string DecodeConnectString(string hex)
        {
            byte[] ciphertext = HexToBytes(hex);
            if (ciphertext.Length == 0) return string.Empty;
            if (ciphertext.Length % 16 != 0)
                throw new CryptographicException("connectStr ciphertext is not AES block aligned");

            byte[] key = Encoding.ASCII.GetBytes(ProtocolConstants.CsapId);
            byte[] plaintext;
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    plaintext = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }
            return Encoding.UTF8.GetString(Unpad(plaintext)).Trim();
        }

        internal static string EncodeConnectStringForSelfTest(string plaintext)
        {
            byte[] key = Encoding.ASCII.GetBytes(ProtocolConstants.CsapId);
            return BytesToHex(EncryptPadded(Encoding.UTF8.GetBytes(plaintext), key));
        }

        private static string EncryptBase64(byte[] plaintext, byte[] key)
        {
            return Convert.ToBase64String(EncryptPadded(plaintext, key));
        }

        private static byte[] EncryptPadded(byte[] plaintext, byte[] key)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                    return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }
        }

        private static byte[] Unpad(byte[] data)
        {
            if (data.Length == 0) return data;
            int count = data[data.Length - 1];
            if (count >= 1 && count <= 16 && count <= data.Length)
            {
                bool valid = true;
                for (int i = data.Length - count; i < data.Length; i++)
                    if (data[i] != count) valid = false;
                if (valid)
                {
                    byte[] result = new byte[data.Length - count];
                    Buffer.BlockCopy(data, 0, result, 0, result.Length);
                    return result;
                }
            }
            int length = data.Length;
            while (length > 0 && data[length - 1] == 0) length--;
            byte[] unpadded = new byte[length];
            Buffer.BlockCopy(data, 0, unpadded, 0, length);
            return unpadded;
        }

        private static byte[] HexToBytes(string value)
        {
            string hex = value ?? string.Empty;
            if ((hex.Length & 1) != 0) hex = "0" + hex;
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        private static string BytesToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }

        private static byte[] BuildRequestKey()
        {
            byte[] key = new byte[16];
            byte[] source = Encoding.ASCII.GetBytes("SuYan@@Zte");
            Buffer.BlockCopy(source, 0, key, 0, source.Length);
            return key;
        }
    }
}
