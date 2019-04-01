using System;
using System.Net;
using System.Net.Http;

namespace BaGet.Protocol
{
    public class ProtocolException : Exception
    {
        public ProtocolException(
            string message,
            HttpMethod method,
            string requestUri,
            HttpStatusCode statusCode,
            string reasonPhrase) : base(message)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            RequestUri = requestUri ?? throw new ArgumentNullException(nameof(requestUri));
            StatusCode = statusCode;
            ReasonPhrase = reasonPhrase ?? throw new ArgumentNullException(nameof(reasonPhrase));
        }

        public HttpMethod Method { get; }
        public string RequestUri { get; }
        public HttpStatusCode StatusCode { get; }
        public string ReasonPhrase { get; }
    }
}
