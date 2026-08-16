using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace EcloudLite.Protocol
{
    internal sealed class PathBPackets
    {
        public byte[] Ztec50 { get; set; }
        public byte[] Auth220 { get; set; }
        public byte[] Client116 { get; set; }
        public byte[] Client108 { get; set; }
        public byte[] Header163 { get; set; }
        public byte[] Redq163 { get; set; }
        public byte[] Header128 { get; set; }
    }

    internal static class PathBProtocol
    {
        private const string Ztec50Base64 = "WlRFQywAZQAAAF+T6XHcAAAAAAAAAAAAAAAAAAAAAAAAAAMAiwAAAAAAAAAAAAAAAAA=";
        private const string Auth220Base64 = "7BMAACQJjIVUADvdM3ifctOKT79jMGQ4OGNmYy05MTM1LTRlMjQtOGZlOS04YTNlMmFmNDkxNzIAAAAAavqraYKIA696QZcdN6IKeTkDy1lV8HDG3p4+7XDtQG/7FxI/H3+yvDtL/hmpZ6OAHT48IDGwqHJvh75YIL/TqGbWge7V06/hlf714guzIjgh8xFfTZBMCvyJKOM4Jo+yJlHWl45Vn+zugWnpzN/EftRsKcWpcuh+iB9dBwompO0BAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";
        private const string Client116Base64 = "AQAAAAAAAACf6gAAAgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
        private const string Client108Base64 = "GgFoAJ/qAQAAAAAAJAmMhVQAO90zeJ9y04pPvwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        private const string Header163Base64 = "CgGjAA==";
        private const string Redq163Base64 = "UkVEUQIAAAACAAAAkwAAAAAAAAABAAAAAAABAAAAjwAAAAAAAAAAAAAAABQAAAAAAQA5MTcyMzM0MWMwZDg4Y2ZjLTkxMzUtNGUyNC04ZmU5LThhM2UyYWY0OTE3MgAAAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAOkDAA==";
        private const string Header128Base64 = "CgGAAA==";
        private const string RedqPlaceholder = "91723341c0d88cfc-9135-4e24-8fe9-8a3e2af49172";

        public static PathBPackets BuildPackets(ConnectParameters parameters)
        {
            if (parameters == null || !parameters.IsComplete) throw new InvalidOperationException("连接参数缺少 hv6/k/vmid");
            IPAddress ip = IPAddress.Parse(parameters.Hv6.Split('%')[0]);
            byte[] ipBytes = ip.GetAddressBytes();
            if (ipBytes.Length != 16) throw new InvalidOperationException("hv6 不是 IPv6 地址");
            byte[] vmidBytes = Encoding.ASCII.GetBytes(parameters.Vmid);
            byte[] keyBytes = Encoding.ASCII.GetBytes(parameters.Key);
            if (vmidBytes.Length != 36 || keyBytes.Length != 8) throw new InvalidOperationException("vmid 或 k 长度不符合 Path B 格式");

            byte[] auth = Decode(Auth220Base64);
            WriteUInt32LE(auth, 0, (uint)parameters.Port);
            Buffer.BlockCopy(ipBytes, 0, auth, 4, 16);
            Buffer.BlockCopy(vmidBytes, 0, auth, 20, 36);
            Array.Clear(auth, 56, 4);

            byte[] c116 = Decode(Client116Base64);
            WriteUInt32LE(c116, 8, (uint)parameters.ProxySport);

            byte[] c108 = Decode(Client108Base64);
            WriteUInt16LE(c108, 4, (ushort)parameters.ProxySport);
            Buffer.BlockCopy(ipBytes, 0, c108, 12, 16);
            for (int i = 0; i + 4 <= c108.Length; i++)
                if (ReadUInt32LE(c108, i) == 60063) WriteUInt32LE(c108, i, (uint)parameters.ProxySport);

            byte[] redq = Decode(Redq163Base64);
            byte[] placeholder = Encoding.ASCII.GetBytes(RedqPlaceholder);
            int slot = IndexOf(redq, placeholder);
            if (slot < 0) throw new InvalidOperationException("REDQ 模板缺少动态字段槽位");
            Buffer.BlockCopy(keyBytes, 0, redq, slot, keyBytes.Length);
            Buffer.BlockCopy(vmidBytes, 0, redq, slot + keyBytes.Length, vmidBytes.Length);

            return new PathBPackets
            {
                Ztec50 = Decode(Ztec50Base64),
                Auth220 = auth,
                Client116 = c116,
                Client108 = c108,
                Header163 = Decode(Header163Base64),
                Redq163 = redq,
                Header128 = Decode(Header128Base64)
            };
        }

        public static byte[] VendorHeader(int payloadLength)
        {
            return new byte[] { 0x0a, 0x01, (byte)(payloadLength & 0xff), (byte)((payloadLength >> 8) & 0xff) };
        }

        public static byte[] BuildSpiceFrame(ulong serial, ushort type, byte[] body, int padTo)
        {
            byte[] frame = new byte[16 + body.Length];
            WriteUInt64LE(frame, 0, serial);
            WriteUInt16LE(frame, 8, type);
            WriteUInt16LE(frame, 10, (ushort)body.Length);
            WriteUInt32LE(frame, 12, 0);
            Buffer.BlockCopy(body, 0, frame, 16, body.Length);
            if (padTo > frame.Length) Array.Resize(ref frame, padTo);
            return frame;
        }

        public static byte[] HeartAck(ulong serial)
        {
            return BuildSpiceFrame(serial, 0x79, new byte[] { 0 }, 20);
        }

        public static byte[] AgentHeartbeat(ulong serial)
        {
            byte[] body = new byte[20];
            WriteUInt32BE(body, 0, 1);
            WriteUInt32BE(body, 4, 0x7d);
            WriteUInt64BE(body, 8, 0);
            WriteUInt32BE(body, 16, 0);
            return BuildSpiceFrame(serial, 0x6b, body, 0);
        }

        public static List<SpiceFrame> ParseVendorFrames(byte[] buffer)
        {
            List<SpiceFrame> frames = new List<SpiceFrame>();
            int i = 0;
            while (i + 4 <= buffer.Length)
            {
                if (buffer[i] != 0x0a || buffer[i + 1] != 0x01)
                {
                    i++;
                    continue;
                }
                int payloadLength = buffer[i + 2] | (buffer[i + 3] << 8);
                if (i + 4 + payloadLength > buffer.Length) break;
                int offset = i + 4;
                int end = offset + payloadLength;
                while (offset + 16 <= end)
                {
                    ulong serial = ReadUInt64LE(buffer, offset);
                    ushort type = ReadUInt16LE(buffer, offset + 8);
                    ushort size = ReadUInt16LE(buffer, offset + 10);
                    if (offset + 16 + size > end) break;
                    frames.Add(new SpiceFrame { Serial = serial, Type = type, Size = size });
                    offset += 16 + size;
                }
                i += 4 + payloadLength;
            }
            return frames;
        }

        public sealed class SpiceFrame
        {
            public ulong Serial { get; set; }
            public ushort Type { get; set; }
            public ushort Size { get; set; }
        }

        private static byte[] Decode(string value) { return Convert.FromBase64String(value); }
        private static int IndexOf(byte[] source, byte[] needle)
        {
            for (int i = 0; i <= source.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++) if (source[i + j] != needle[j]) { match = false; break; }
                if (match) return i;
            }
            return -1;
        }
        private static ushort ReadUInt16LE(byte[] b, int o) { return (ushort)(b[o] | (b[o + 1] << 8)); }
        private static uint ReadUInt32LE(byte[] b, int o) { return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)); }
        private static ulong ReadUInt64LE(byte[] b, int o) { return ReadUInt32LE(b, o) | ((ulong)ReadUInt32LE(b, o + 4) << 32); }
        private static void WriteUInt16LE(byte[] b, int o, ushort v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        private static void WriteUInt32LE(byte[] b, int o, uint v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
        private static void WriteUInt32BE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        private static void WriteUInt64LE(byte[] b, int o, ulong v) { WriteUInt32LE(b, o, (uint)v); WriteUInt32LE(b, o + 4, (uint)(v >> 32)); }
        private static void WriteUInt64BE(byte[] b, int o, ulong v) { WriteUInt32BE(b, o, (uint)(v >> 32)); WriteUInt32BE(b, o + 4, (uint)v); }
    }
}
