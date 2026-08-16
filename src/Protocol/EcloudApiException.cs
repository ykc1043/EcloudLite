using System;
using System.Collections.Generic;

namespace EcloudLite.Protocol
{
    internal sealed class EcloudApiException : Exception
    {
        public string ErrorCode { get; private set; }
        public Dictionary<string, object> ResponseObject { get; private set; }

        public EcloudApiException(string errorCode, string message, Dictionary<string, object> responseObject)
            : base(message)
        {
            ErrorCode = errorCode ?? string.Empty;
            ResponseObject = responseObject ?? new Dictionary<string, object>();
        }
    }
}
