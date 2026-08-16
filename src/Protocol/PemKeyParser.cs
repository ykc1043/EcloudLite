using System;
using System.Security.Cryptography;

namespace EcloudLite.Protocol
{
    internal static class PemKeyParser
    {
        public static RSAParameters ReadPublicKey(string pem)
        {
            DerReader root = new DerReader(PemToDer(pem)).ReadSequence();
            root.ReadSequence();
            byte[] bitString = root.ReadBitString();
            DerReader rsa = new DerReader(bitString).ReadSequence();
            return new RSAParameters
            {
                Modulus = NormalizeInteger(rsa.ReadInteger()),
                Exponent = NormalizeInteger(rsa.ReadInteger())
            };
        }

        public static RSAParameters ReadPrivateKey(string pem)
        {
            DerReader root = new DerReader(PemToDer(pem)).ReadSequence();
            root.ReadInteger();
            root.ReadSequence();
            byte[] privateOctets = root.ReadOctetString();
            DerReader rsa = new DerReader(privateOctets).ReadSequence();
            rsa.ReadInteger();

            byte[] modulus = NormalizeInteger(rsa.ReadInteger());
            int modulusSize = modulus.Length;
            int halfSize = modulusSize / 2;
            return new RSAParameters
            {
                Modulus = modulus,
                Exponent = NormalizeInteger(rsa.ReadInteger()),
                D = PadLeft(NormalizeInteger(rsa.ReadInteger()), modulusSize),
                P = PadLeft(NormalizeInteger(rsa.ReadInteger()), halfSize),
                Q = PadLeft(NormalizeInteger(rsa.ReadInteger()), halfSize),
                DP = PadLeft(NormalizeInteger(rsa.ReadInteger()), halfSize),
                DQ = PadLeft(NormalizeInteger(rsa.ReadInteger()), halfSize),
                InverseQ = PadLeft(NormalizeInteger(rsa.ReadInteger()), halfSize)
            };
        }

        private static byte[] PemToDer(string pem)
        {
            string body = pem
                .Replace("-----BEGIN PUBLIC KEY-----", string.Empty)
                .Replace("-----END PUBLIC KEY-----", string.Empty)
                .Replace("-----BEGIN PRIVATE KEY-----", string.Empty)
                .Replace("-----END PRIVATE KEY-----", string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Trim();
            return Convert.FromBase64String(body);
        }

        private static byte[] NormalizeInteger(byte[] value)
        {
            int offset = 0;
            while (offset < value.Length - 1 && value[offset] == 0) offset++;
            if (offset == 0) return value;
            byte[] result = new byte[value.Length - offset];
            Buffer.BlockCopy(value, offset, result, 0, result.Length);
            return result;
        }

        private static byte[] PadLeft(byte[] value, int size)
        {
            if (value.Length == size) return value;
            if (value.Length > size)
            {
                byte[] truncated = new byte[size];
                Buffer.BlockCopy(value, value.Length - size, truncated, 0, size);
                return truncated;
            }
            byte[] result = new byte[size];
            Buffer.BlockCopy(value, 0, result, size - value.Length, value.Length);
            return result;
        }

        private sealed class DerReader
        {
            private readonly byte[] _data;
            private int _position;

            public DerReader(byte[] data)
            {
                _data = data;
                _position = 0;
            }

            public DerReader ReadSequence()
            {
                return new DerReader(ReadValue(0x30));
            }

            public byte[] ReadInteger()
            {
                return ReadValue(0x02);
            }

            public byte[] ReadOctetString()
            {
                return ReadValue(0x04);
            }

            public byte[] ReadBitString()
            {
                byte[] value = ReadValue(0x03);
                if (value.Length == 0 || value[0] != 0)
                    throw new CryptographicException("Unsupported DER bit string");
                byte[] result = new byte[value.Length - 1];
                Buffer.BlockCopy(value, 1, result, 0, result.Length);
                return result;
            }

            private byte[] ReadValue(int expectedTag)
            {
                if (_position >= _data.Length || _data[_position++] != expectedTag)
                    throw new CryptographicException("Unexpected DER tag");
                int length = ReadLength();
                if (length < 0 || _position + length > _data.Length)
                    throw new CryptographicException("Invalid DER length");
                byte[] result = new byte[length];
                Buffer.BlockCopy(_data, _position, result, 0, length);
                _position += length;
                return result;
            }

            private int ReadLength()
            {
                if (_position >= _data.Length)
                    throw new CryptographicException("Missing DER length");
                int first = _data[_position++];
                if ((first & 0x80) == 0) return first;
                int count = first & 0x7f;
                if (count <= 0 || count > 4 || _position + count > _data.Length)
                    throw new CryptographicException("Unsupported DER length");
                int length = 0;
                for (int i = 0; i < count; i++) length = (length << 8) | _data[_position++];
                return length;
            }
        }
    }
}
