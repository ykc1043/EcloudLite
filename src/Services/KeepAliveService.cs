using System;
using System.Threading;
using EcloudLite.Infrastructure;
using EcloudLite.Models;

namespace EcloudLite.Services
{
    internal sealed class KeepAliveStatus
    {
        public bool Running { get; set; }
        public bool StopRequested { get; set; }
        public string MachineName { get; set; }
        public string InstanceId { get; set; }
        public string Stage { get; set; }
        public int Round { get; set; }
        public int HeartsThisRound { get; set; }
        public int TotalHearts { get; set; }
        public int SuccessfulRounds { get; set; }
        public int FailedRounds { get; set; }
        public string LastConnection { get; set; }
        public string LastUptime { get; set; }
        public DateTime? LastSuccessLocal { get; set; }
        public DateTime? NextRoundLocal { get; set; }
        public DateTime StartedLocal { get; set; }

        public KeepAliveStatus Clone()
        {
            return (KeepAliveStatus)MemberwiseClone();
        }
    }

    internal sealed class KeepAliveService : IDisposable
    {
        public const int HeartListenSeconds = 60;
        public const int RoundIntervalSeconds = 300;

        private readonly object _gate = new object();
        private readonly ConnectionService _connectionService;
        private readonly PathBHandshakeService _pathBHandshakeService;
        private readonly DesktopService _desktopService;
        private ManualResetEvent _stopSignal;
        private Thread _worker;
        private KeepAliveStatus _status = NewStoppedStatus();

        public KeepAliveService(
            ConnectionService connectionService,
            PathBHandshakeService pathBHandshakeService,
            DesktopService desktopService)
        {
            _connectionService = connectionService;
            _pathBHandshakeService = pathBHandshakeService;
            _desktopService = desktopService;
        }

        public event Action<KeepAliveStatus> StatusChanged;

        public bool IsRunning
        {
            get { lock (_gate) return _status.Running; }
        }

        public KeepAliveStatus CurrentStatus
        {
            get { lock (_gate) return _status.Clone(); }
        }

        public void Start(Desktop desktop)
        {
            if (desktop == null) throw new ArgumentNullException("desktop");
            if (!string.Equals(desktop.OriginCompanyCode, "CMSSZTE", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("保活当前仅支持 CMSSZTE 后端");

            Desktop target = CloneDesktop(desktop);
            KeepAliveStatus started;
            lock (_gate)
            {
                if (_status.Running) throw new InvalidOperationException("保活已经在运行");
                if (_stopSignal != null) _stopSignal.Close();
                ManualResetEvent signal = new ManualResetEvent(false);
                _stopSignal = signal;
                _status = new KeepAliveStatus
                {
                    Running = true,
                    MachineName = target.MachineName ?? string.Empty,
                    InstanceId = target.InstanceId ?? string.Empty,
                    Stage = "正在启动",
                    LastConnection = "尚未连接",
                    LastUptime = "尚未查询",
                    StartedLocal = DateTime.Now
                };
                started = _status.Clone();
                _worker = new Thread(new ThreadStart(delegate { Run(target, signal); }));
                _worker.Name = "EcloudLite-PathB-KeepAlive";
                _worker.IsBackground = true;
                _worker.Start();
            }

            Logger.Info("PATHB_KEEPALIVE", "keepalive start instance=" + Logger.ShortId(target.InstanceId) +
                " heart_listen_s=" + HeartListenSeconds + " interval_s=" + RoundIntervalSeconds +
                " runtime_required=false multi_device=false production_claim=false");
            Publish(started);
        }

        public void Stop(string reason, bool waitForExit)
        {
            Thread worker;
            KeepAliveStatus stopping;
            lock (_gate)
            {
                if (!_status.Running) return;
                _status.StopRequested = true;
                _status.Stage = "正在停止";
                stopping = _status.Clone();
                worker = _worker;
                if (_stopSignal != null) _stopSignal.Set();
            }
            Logger.Info("PATHB_KEEPALIVE", "stop requested reason=" + Logger.Redact(reason ?? "user") +
                " round=" + stopping.Round + " total_hearts=" + stopping.TotalHearts);
            Publish(stopping);

            if (waitForExit && worker != null && worker != Thread.CurrentThread && !worker.Join(5000))
                Logger.Warn("PATHB_KEEPALIVE", "worker did not exit within 5000ms; background cleanup will continue");
        }

        public void Dispose()
        {
            Stop("service dispose", true);
        }

        private void Run(Desktop desktop, ManualResetEvent stopSignal)
        {
            try
            {
                while (!stopSignal.WaitOne(0))
                {
                    int round;
                    int heartsBeforeRound;
                    lock (_gate)
                    {
                        _status.Round++;
                        round = _status.Round;
                        heartsBeforeRound = _status.TotalHearts;
                        _status.HeartsThisRound = 0;
                        _status.NextRoundLocal = null;
                        _status.Stage = "第 " + round + " 轮：获取连接参数";
                    }
                    PublishCurrent();
                    Logger.Info("PATHB_KEEPALIVE", "round start round=" + round + " instance=" +
                        Logger.ShortId(desktop.InstanceId) + " totals_success=" + CurrentStatus.SuccessfulRounds +
                        " totals_fail=" + CurrentStatus.FailedRounds);

                    try
                    {
                        ConnectResult connect = _connectionService.RequestConnectInfo(desktop);
                        if (stopSignal.WaitOne(0)) break;

                        SetStage("第 " + round + " 轮：监听并回复心跳");
                        PathBHandshakeResult path = _pathBHandshakeService.KeepAliveRound(
                            connect,
                            HeartListenSeconds * 1000,
                            delegate { return stopSignal.WaitOne(0); },
                            delegate(PathBHandshakeResult progress)
                            {
                                lock (_gate)
                                {
                                    _status.HeartsThisRound = progress.HeartCount;
                                    _status.TotalHearts = heartsBeforeRound + progress.HeartCount;
                                    _status.LastConnection = "连接中：TLS " + (progress.TlsVersion ?? "-") +
                                        "，REDQ " + progress.RedqBytes + " 字节，心跳 " + progress.HeartCount;
                                }
                                PublishCurrent();
                            });

                        if (path.Cancelled || stopSignal.WaitOne(0)) break;

                        string uptime = "查询失败";
                        try
                        {
                            SetStage("第 " + round + " 轮：验证在线时长");
                            uptime = _desktopService.GetUptime(desktop);
                            Logger.Info("PATHB_KEEPALIVE", "round uptime ok round=" + round + " value=" + uptime);
                        }
                        catch (Exception uptimeException)
                        {
                            Logger.Warn("PATHB_KEEPALIVE", "round uptime failed round=" + round + " exception=" +
                                uptimeException.GetType().FullName + " message=" + Logger.Redact(uptimeException.Message));
                        }

                        bool success = path.HeartKeepAliveOk;
                        lock (_gate)
                        {
                            _status.HeartsThisRound = path.HeartCount;
                            _status.TotalHearts = heartsBeforeRound + path.HeartCount;
                            _status.LastUptime = uptime;
                            _status.LastConnection = string.Format(
                                "{0}：TLS {1}，REDQ {2} 字节，心跳 {3}，耗时 {4:0.0} 秒",
                                success ? "成功" : "未达到保活条件",
                                path.TlsVersion ?? "-",
                                path.RedqBytes,
                                path.HeartCount,
                                path.ElapsedMilliseconds / 1000.0);
                            if (success)
                            {
                                _status.SuccessfulRounds++;
                                _status.LastSuccessLocal = DateTime.Now;
                            }
                            else
                            {
                                _status.FailedRounds++;
                            }
                        }
                        Logger.Info("PATHB_KEEPALIVE", "round complete round=" + round + " success=" + success +
                            " tls=" + (path.TlsVersion ?? "-") + " redq_bytes=" + path.RedqBytes +
                            " hearts=" + path.HeartCount + " frames=" + path.FrameCount +
                            " elapsed_ms=" + path.ElapsedMilliseconds + " uptime_ok=" + (uptime != "查询失败") +
                            " production_claim=false");
                    }
                    catch (OperationCanceledException)
                    {
                        if (!stopSignal.WaitOne(0)) throw;
                        break;
                    }
                    catch (Exception exception)
                    {
                        lock (_gate)
                        {
                            _status.FailedRounds++;
                            _status.LastConnection = "失败：" + Logger.Redact(exception.Message);
                            _status.LastUptime = "未查询";
                        }
                        Logger.Exception("PATHB_KEEPALIVE", exception, "round failed round=" + round +
                            " instance=" + Logger.ShortId(desktop.InstanceId));
                    }

                    if (stopSignal.WaitOne(0)) break;
                    lock (_gate)
                    {
                        _status.Stage = "等待下一轮";
                        _status.NextRoundLocal = DateTime.Now.AddSeconds(RoundIntervalSeconds);
                    }
                    KeepAliveStatus waiting = CurrentStatus;
                    Logger.Info("PATHB_KEEPALIVE", "round wait round=" + round + " next_local=" +
                        waiting.NextRoundLocal.Value.ToString("yyyy-MM-dd HH:mm:ss") + " interval_s=" + RoundIntervalSeconds +
                        " totals_success=" + waiting.SuccessfulRounds + " totals_fail=" + waiting.FailedRounds +
                        " total_hearts=" + waiting.TotalHearts);
                    Publish(waiting);
                    if (stopSignal.WaitOne(RoundIntervalSeconds * 1000)) break;
                }
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _status.FailedRounds++;
                    _status.LastConnection = "保活线程异常：" + Logger.Redact(exception.Message);
                }
                Logger.Exception("PATHB_KEEPALIVE", exception, "keepalive worker terminated unexpectedly");
            }
            finally
            {
                KeepAliveStatus finished;
                lock (_gate)
                {
                    _status.Running = false;
                    _status.StopRequested = false;
                    _status.Stage = "已停止";
                    _status.NextRoundLocal = null;
                    finished = _status.Clone();
                    _worker = null;
                }
                Logger.Info("PATHB_KEEPALIVE", "keepalive final instance=" + Logger.ShortId(finished.InstanceId) +
                    " rounds=" + finished.Round + " success=" + finished.SuccessfulRounds +
                    " fail=" + finished.FailedRounds + " total_hearts=" + finished.TotalHearts +
                    " last_connection=" + finished.LastConnection + " last_uptime=" + finished.LastUptime +
                    " production_claim=false");
                Publish(finished);
            }
        }

        private void SetStage(string stage)
        {
            lock (_gate) _status.Stage = stage;
            PublishCurrent();
        }

        private void PublishCurrent()
        {
            Publish(CurrentStatus);
        }

        private void Publish(KeepAliveStatus status)
        {
            Action<KeepAliveStatus> handler = StatusChanged;
            if (handler == null) return;
            try { handler(status); }
            catch { }
        }

        private static KeepAliveStatus NewStoppedStatus()
        {
            return new KeepAliveStatus
            {
                Stage = "未启动",
                LastConnection = "尚未连接",
                LastUptime = "尚未查询"
            };
        }

        private static Desktop CloneDesktop(Desktop desktop)
        {
            return new Desktop
            {
                InstanceId = desktop.InstanceId,
                MachineId = desktop.MachineId,
                MachineName = desktop.MachineName,
                OriginCompanyCode = desktop.OriginCompanyCode,
                ResourcePoolUid = desktop.ResourcePoolUid,
                ResourceStatus = desktop.ResourceStatus,
                CustomLoginParams = desktop.CustomLoginParams,
                RawFields = desktop.RawFields
            };
        }
    }
}
