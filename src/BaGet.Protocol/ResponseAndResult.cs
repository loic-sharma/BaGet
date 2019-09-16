using System;
using System.Net;
using System.Net.Http;
using BaGet.Protocol.Models;

namespace BaGet.Protocol
{
    internal class ResponseAndResult<T>
    {
        public ResponseAndResult(
            HttpMethod method,
            string requestUri,
            HttpStatusCode statusCode,
            string reasonPhrase,
            bool hasResult,
            T result)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            RequestUri = requestUri ?? throw new ArgumentNullException(nameof(requestUri));
            StatusCode = statusCode;
            ReasonPhrase = reasonPhrase ?? throw new ArgumentNullException(nameof(reasonPhrase));
            HasResult = hasResult;
            Result = result;
        }

        public HttpMethod Method { get; }
        public string RequestUri { get; }
        public HttpStatusCode StatusCode { get; }
        public string ReasonPhrase { get; }
        public bool HasResult { get; }
        public T Result { get; }

        public T GetResultOrThrow()
        {
            if (!HasResult)
            {
                throw new ProtocolException(
                    $"The HTTP request failed.{Environment.NewLine}" +
                    $"Request: {Method} {RequestUri}{Environment.NewLine}" +
                    $"Response: {(int)StatusCode} {ReasonPhrase}",
                    Method,
                    RequestUri,
                    StatusCode,
                    ReasonPhrase);
            }

            return Result;
        }
    }
}
