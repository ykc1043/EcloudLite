using System.Collections.Generic;

namespace EcloudLite.Models
{
    internal sealed class Desktop
    {
        public string InstanceId { get; set; }
        public string MachineId { get; set; }
        public string MachineName { get; set; }
        public string OriginCompanyCode { get; set; }
        public string ResourcePoolUid { get; set; }
        public string ResourceStatus { get; set; }
        public object CustomLoginParams { get; set; }
        public Dictionary<string, object> RawFields { get; set; }
    }
}
