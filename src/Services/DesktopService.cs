using System;
using System.Collections.Generic;
using EcloudLite.Infrastructure;
using EcloudLite.Models;
using EcloudLite.Protocol;

namespace EcloudLite.Services
{
    internal sealed class DesktopService
    {
        private readonly EcloudApiClient _client;

        public DesktopService(EcloudApiClient client)
        {
            _client = client;
        }

        public List<Desktop> GetDesktops()
        {
            Dictionary<string, object> response = _client.Post(
                ProtocolConstants.GetDeviceInfo,
                new Dictionary<string, object>
                {
                    { "companyCode", ProtocolConstants.CompanyCode },
                    { "allCompany", true },
                    { "version", "1.0.0" }
                });

            List<Desktop> desktops = new List<Desktop>();
            foreach (object item in JsonValue.Array(response, "machineList"))
            {
                Dictionary<string, object> machine = JsonValue.AsDictionary(item);
                Desktop desktop = new Desktop
                {
                    InstanceId = JsonValue.String(machine, "instanceId"),
                    MachineId = JsonValue.String(machine, "machineId"),
                    MachineName = JsonValue.String(machine, "machineName"),
                    OriginCompanyCode = JsonValue.String(machine, "originCompanyCode"),
                    ResourcePoolUid = JsonValue.String(machine, "resourcePoolUid"),
                    RawFields = new Dictionary<string, object>(machine)
                };
                object custom;
                if (machine.TryGetValue("customLoginParams", out custom))
                {
                    desktop.CustomLoginParams = custom;
                    Logger.Info("DESKTOP", "custom login params shape instance=" + Logger.ShortId(desktop.InstanceId) + " shape=" + JsonValue.Shape(custom));
                }
                if (!string.IsNullOrEmpty(desktop.InstanceId) || !string.IsNullOrEmpty(desktop.MachineId)) desktops.Add(desktop);
            }

            Logger.Info("DESKTOP", "desktop list loaded count=" + desktops.Count);
            PopulateStatuses(desktops);
            return desktops;
        }

        public void PopulateStatuses(List<Desktop> desktops)
        {
            if (desktops == null || desktops.Count == 0) return;
            List<string> instanceIds = new List<string>();
            for (int i = 0; i < desktops.Count; i++)
                if (!string.IsNullOrEmpty(desktops[i].InstanceId)) instanceIds.Add(desktops[i].InstanceId);
            if (instanceIds.Count == 0) return;

            try
            {
                Dictionary<string, object> response = _client.Post(
                    ProtocolConstants.GetDesktopStatus,
                    new Dictionary<string, object> { { "instanceIdList", instanceIds.ToArray() } });
                Dictionary<string, string> statusMap = new Dictionary<string, string>();
                foreach (object item in JsonValue.Array(response, "machineStatusList"))
                {
                    Dictionary<string, object> status = JsonValue.AsDictionary(item);
                    statusMap[JsonValue.String(status, "instanceId")] = JsonValue.String(status, "resourceStatus");
                }
                for (int i = 0; i < desktops.Count; i++)
                {
                    string status;
                    if (statusMap.TryGetValue(desktops[i].InstanceId ?? string.Empty, out status)) desktops[i].ResourceStatus = status;
                }
                Logger.Info("DESKTOP", "desktop statuses loaded count=" + statusMap.Count);
            }
            catch (Exception ex)
            {
                Logger.Exception("DESKTOP", ex, "desktop status query failed; list retained");
            }
        }

        public void Operate(Desktop desktop, string operation)
        {
            if (desktop == null) throw new ArgumentNullException("desktop");
            Logger.Info("DESKTOP", string.Format("operation start op={0} machine={1} instance={2} origin={3}", operation, Logger.ShortId(desktop.MachineId), Logger.ShortId(desktop.InstanceId), desktop.OriginCompanyCode));
            _client.Post(
                ProtocolConstants.ResourceOperate,
                new Dictionary<string, object>
                {
                    { "machineId", desktop.MachineId },
                    { "machineName", desktop.MachineName },
                    { "operate", operation },
                    { "deviceUid", _client.DeviceUid },
                    { "resourcePoolUid", desktop.ResourcePoolUid ?? string.Empty },
                    { "sdkType", 4 }
                });
            Logger.Info("DESKTOP", "operation accepted op=" + operation + " instance=" + Logger.ShortId(desktop.InstanceId));
        }

        public string GetUptime(Desktop desktop)
        {
            if (desktop == null) throw new ArgumentNullException("desktop");
            object response = _client.PostRaw(
                ProtocolConstants.DesktopUptime,
                new Dictionary<string, object> { { "instanceId", desktop.InstanceId } });
            Dictionary<string, object> dictionary = JsonValue.AsDictionary(response);
            string uptime = string.Empty;
            if (dictionary.Count > 0)
            {
                uptime = First(dictionary, "uptime", "upTime", "runningTime", "duration");
            }
            else if (response != null)
            {
                uptime = Convert.ToString(response);
            }
            if (string.IsNullOrEmpty(uptime)) throw new InvalidOperationException("服务端未返回运行时长，桌面可能未开机");
            Logger.Info("DESKTOP", "uptime instance=" + Logger.ShortId(desktop.InstanceId) + " value=" + uptime);
            return uptime;
        }

        public static string BackendDescription(string origin)
        {
            string value = (origin ?? string.Empty).Trim().ToUpperInvariant();
            if (value == "CMSSZTE") return "CMSSZTE / CAG 8899 / Path B";
            if (value == "ZTE" || value == "ZTEECLOUD") return "ZTE / SCG-SPICE candidate";
            if (value == "H3C") return "H3C vendor backend";
            if (value == "YUNTIAN") return "YUNTIAN / VRTC candidate";
            return string.IsNullOrEmpty(value) ? "Unknown" : value + " / unclassified";
        }

        private static string First(Dictionary<string, object> dictionary, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                string value = JsonValue.String(dictionary, keys[i]);
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return string.Empty;
        }
    }
}
