using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using EcloudLite.Infrastructure;
using EcloudLite.Protocol;

namespace EcloudLite.Services
{
    internal sealed class PathBHandshakeResult
    {
        public bool ZtecOk { get; set; }
        public bool AuthOk { get; set; }
        public bool TlsOk { get; set; }
        public bool RedqOk { get; set; }
        public bool TicketOk { get; set; }
        public int HeartCount { get; set; }
        public int FrameCount { get; set; }
        public string TlsVersion { get; set; }
        public int RedqBytes { get; set; }
        public int PostTicketBytes { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public bool Cancelled { get; set; }

        public bool Success { get { return ZtecOk && AuthOk && TlsOk && RedqOk && TicketOk; } }
        public bool HeartKeepAliveOk { get { return Success && HeartCount >= 2; } }
    }

    internal sealed class PathBHandshakeService
    {
        public PathBHandshakeResult Probe(ConnectResult connect)
        {
            return Execute(connect, 26000, true, null, null, "PATHB");
        }

        public PathBHandshakeResult KeepAliveRound(
            ConnectResult connect,
            int durationMs,
            Func<bool> shouldStop,
            Action<PathBHandshakeResult> progress)
        {
            if (durationMs < 1000) throw new ArgumentOutOfRangeException("durationMs");
            return Execute(connect, durationMs, false, shouldStop, progress, "PATHB_KEEPALIVE");
        }

        private static PathBHandshakeResult Execute(
            ConnectResult connect,
            int durationMs,
            bool stopAfterTwoHearts,
            Func<bool> shouldStop,
            Action<PathBHandshakeResult> progress,
            string logCategory)
        {
            if (connect == null) throw new ArgumentNullException("connect");
            if (string.IsNullOrEmpty(connect.CagHost) || connect.CagPort <= 0) throw new InvalidOperationException("CAG 目标缺失");
            if (connect.Parameters == null || !connect.Parameters.IsComplete) throw new InvalidOperationException("短期连接参数不完整");

            PathBPackets packets = PathBProtocol.BuildPackets(connect.Parameters);
            PathBHandshakeResult result = new PathBHandshakeResult();
            Stopwatch total = Stopwatch.StartNew();
            TcpClient client = null;
            NetworkStream network = null;
            SslStream tls = null;
            try
            {
                ThrowIfStopped(shouldStop);
                Logger.Info(logCategory, "stage=tcp_connect start cag_port=" + connect.CagPort);
                client = Connect(connect.CagHost, connect.CagPort, 5000);
                client.NoDelay = true;
                network = client.GetStream();
                Logger.Info(logCategory, "stage=tcp_connect ok elapsed_ms=" + total.ElapsedMilliseconds);

                ThrowIfStopped(shouldStop);
                Write(network, packets.Ztec50);
                byte[] ack50 = ReadExact(network, 50, 5000);
                result.ZtecOk = ack50.Length == 50 && StartsWithAscii(ack50, "ZTEC");
                Logger.Info(logCategory, "stage=ztec50 recv=" + ack50.Length + " ok=" + result.ZtecOk + " elapsed_ms=" + total.ElapsedMilliseconds);
                if (!result.ZtecOk) throw new InvalidOperationException("ZTEC50 确认失败");

                ThrowIfStopped(shouldStop);
                Write(network, packets.Auth220);
                byte[] ack36 = ReadExact(network, 36, 5000);
                result.AuthOk = ack36.Length == 36;
                Logger.Info(logCategory, "stage=auth220 recv=" + ack36.Length + " ok=" + result.AuthOk + " elapsed_ms=" + total.ElapsedMilliseconds);
                if (!result.AuthOk) throw new InvalidOperationException("auth220 确认失败");

                Write(network, packets.Client116);
                byte[] preTls = ReadAvailable(network, 400, 400, 4096);
                Logger.Debug(logCategory, "stage=pre_tls_116 recv=" + preTls.Length);

                ThrowIfStopped(shouldStop);
                tls = new SslStream(network, false, ValidateServerCertificate);
                Stopwatch tlsWatch = Stopwatch.StartNew();
                // This machine's .NET Framework rejects SslProtocols.None before ClientHello.
                // CAG accepts the TLS 1.2 compatibility path; Schannel may negotiate newer
                // versions only on runtimes that expose system-default protocol selection.
                tls.AuthenticateAsClient(connect.CagHost, null, SslProtocols.Tls12, false);
                result.TlsOk = tls.IsAuthenticated && tls.IsEncrypted;
                result.TlsVersion = tls.SslProtocol.ToString();
                Logger.Info(logCategory, "stage=tls ok=" + result.TlsOk + " protocol=" + result.TlsVersion + " elapsed_ms=" + tlsWatch.ElapsedMilliseconds);
                if (!result.TlsOk) throw new InvalidOperationException("TLS 握手未建立加密通道");

                Write(tls, packets.Client108);
                byte[] after108 = ReadAvailable(tls, 500, 500, 8192);
                Logger.Debug(logCategory, "stage=client108 sent=" + packets.Client108.Length + " recv=" + after108.Length);

                Write(tls, packets.Header163);
                byte[] afterHeader = ReadAvailable(tls, 300, 300, 8192);
                Logger.Debug(logCategory, "stage=redq_header sent=" + packets.Header163.Length + " recv=" + afterHeader.Length);

                Write(tls, packets.Redq163);
                byte[] redq = ReadAvailable(tls, 2000, 400, 65536);
                result.RedqBytes = redq.Length;
                result.RedqOk = redq.Length >= 100 && ContainsAscii(redq, "REDQ");
                Logger.Info(logCategory, "stage=redq recv=" + redq.Length + " ok=" + result.RedqOk + " elapsed_ms=" + total.ElapsedMilliseconds);
                if (!result.RedqOk) throw new InvalidOperationException("REDQ 服务端响应未通过验证");

                ThrowIfStopped(shouldStop);
                Write(tls, packets.Header128);
                Write(tls, new byte[128]);
                byte[] postTicket = ReadAvailable(tls, 1500, 400, 65536);
                result.PostTicketBytes = postTicket.Length;
                result.TicketOk = true;
                Logger.Info(logCategory, "stage=ticket128 mode=zeros sent=132 recv=" + postTicket.Length + " elapsed_ms=" + total.ElapsedMilliseconds);

                byte[] nudge = PathBProtocol.AgentHeartbeat(100);
                Write(tls, PathBProtocol.VendorHeader(nudge.Length));
                Write(tls, nudge);
                Logger.Info(logCategory, "stage=heart_listen start seconds=" + (durationMs / 1000.0).ToString("0.0") +
                    " nudge_len=" + (nudge.Length + 4) + " stop_after_two=" + stopAfterTwoHearts);
                ListenForHearts(tls, result, postTicket, durationMs, stopAfterTwoHearts, shouldStop, progress, logCategory);
                Logger.Info(logCategory, "stage=heart_listen complete hearts=" + result.HeartCount + " frames=" + result.FrameCount +
                    " cancelled=" + result.Cancelled + " elapsed_ms=" + total.ElapsedMilliseconds);

                result.ElapsedMilliseconds = total.ElapsedMilliseconds;
                Logger.Info(logCategory, "session complete success=" + result.Success + " heart_keepalive_ok=" + result.HeartKeepAliveOk +
                    " heart_observed=" + (result.HeartCount > 0) + " cancelled=" + result.Cancelled +
                    " elapsed_ms=" + result.ElapsedMilliseconds + " production_claim=false");
                return result;
            }
            finally
            {
                result.ElapsedMilliseconds = total.ElapsedMilliseconds;
                if (tls != null) { try { tls.Close(); } catch { } }
                else if (network != null) { try { network.Close(); } catch { } }
                if (client != null) { try { client.Close(); } catch { } }
            }
        }

        private static void ListenForHearts(
            SslStream tls,
            PathBHandshakeResult result,
            byte[] initial,
            int durationMs,
            bool stopAfterTwoHearts,
            Func<bool> shouldStop,
            Action<PathBHandshakeResult> progress,
            string logCategory)
        {
            List<byte> buffer = new List<byte>();
            if (initial != null && initial.Length > 0) buffer.AddRange(initial);
            Stopwatch watch = Stopwatch.StartNew();
            HashSet<ulong> acknowledged = new HashSet<ulong>();
            while (watch.ElapsedMilliseconds < durationMs)
            {
                if (IsStopped(shouldStop))
                {
                    result.Cancelled = true;
                    break;
                }
                int remaining = durationMs - (int)watch.ElapsedMilliseconds;
                byte[] chunk = ReadAvailable(tls, Math.Min(1500, Math.Max(100, remaining)), 300, 65536);
                if (chunk.Length > 0) buffer.AddRange(chunk);
                List<PathBProtocol.SpiceFrame> frames = PathBProtocol.ParseVendorFrames(buffer.ToArray());
                result.FrameCount = Math.Max(result.FrameCount, frames.Count);
                for (int i = 0; i < frames.Count; i++)
                {
                    PathBProtocol.SpiceFrame frame = frames[i];
                    if (frame.Type != 0x74 || acknowledged.Contains(frame.Serial)) continue;
                    byte[] ack = PathBProtocol.HeartAck(frame.Serial);
                    Write(tls, PathBProtocol.VendorHeader(ack.Length));
                    Write(tls, ack);
                    acknowledged.Add(frame.Serial);
                    result.HeartCount++;
                    Logger.Info(logCategory, "heart ack type=0x79 serial=" + frame.Serial + " ack_len=" + (ack.Length + 4) +
                        " count=" + result.HeartCount + " listen_elapsed_ms=" + watch.ElapsedMilliseconds);
                    if (progress != null) progress(result);
                }
                if (stopAfterTwoHearts && result.HeartCount >= 2) break;
            }
        }

        private static void ThrowIfStopped(Func<bool> shouldStop)
        {
            if (IsStopped(shouldStop)) throw new OperationCanceledException("Path B 会话已取消");
        }

        private static bool IsStopped(Func<bool> shouldStop)
        {
            if (shouldStop == null) return false;
            try { return shouldStop(); }
            catch { return false; }
        }

        private static TcpClient Connect(string host, int port, int timeoutMs)
        {
            TcpClient client = new TcpClient();
            IAsyncResult pending = client.BeginConnect(host, port, null, null);
            if (!pending.AsyncWaitHandle.WaitOne(timeoutMs))
            {
                client.Close();
                throw new TimeoutException("CAG TCP 连接超时");
            }
            client.EndConnect(pending);
            return client;
        }

        private static void Write(Stream stream, byte[] data)
        {
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }

        private static byte[] ReadExact(Stream stream, int count, int timeoutMs)
        {
            stream.ReadTimeout = timeoutMs;
            byte[] result = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(result, offset, count - offset);
                if (read <= 0) break;
                offset += read;
            }
            if (offset == result.Length) return result;
            byte[] shortResult = new byte[offset];
            Buffer.BlockCopy(result, 0, shortResult, 0, offset);
            return shortResult;
        }

        private static byte[] ReadAvailable(Stream stream, int firstTimeoutMs, int idleTimeoutMs, int maxBytes)
        {
            MemoryStream result = new MemoryStream();
            byte[] buffer = new byte[8192];
            bool first = true;
            while (result.Length < maxBytes)
            {
                stream.ReadTimeout = first ? firstTimeoutMs : idleTimeoutMs;
                try
                {
                    int wanted = Math.Min(buffer.Length, maxBytes - (int)result.Length);
                    int read = stream.Read(buffer, 0, wanted);
                    if (read <= 0) break;
                    result.Write(buffer, 0, read);
                    first = false;
                }
                catch (IOException)
                {
                    break;
                }
            }
            return result.ToArray();
        }

        private static bool StartsWithAscii(byte[] data, string value)
        {
            if (data.Length < value.Length) return false;
            for (int i = 0; i < value.Length; i++) if (data[i] != (byte)value[i]) return false;
            return true;
        }

        private static bool ContainsAscii(byte[] data, string value)
        {
            byte[] needle = System.Text.Encoding.ASCII.GetBytes(value);
            for (int i = 0; i <= data.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++) if (data[i + j] != needle[j]) { match = false; break; }
                if (match) return true;
            }
            return false;
        }

        private static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors)
        {
            return true;
        }
    }
}
