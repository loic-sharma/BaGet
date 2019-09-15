using System;
using System.Net;
using System.Net.Http;

namespace BaGet.Protocol.Models
{
    /// <summary>
    /// An exception that is thrown when an API has returned an unexpected result.
    /// </summary>
    public class ProtocolException : Exception
    {
        /// <summary>
        /// Create a new <see cref="ProtocolException"/>.
        /// </summary>
        /// <param name="message">The HTTP response message.</param>
        /// <param name="method">The HTTP request method.</param>
        /// <param name="requestUri">The URI that was requested.</param>
        /// <param name="statusCode">The response status code.</param>
        /// <param name="reasonPhrase">The HTTP reason phrase.</param>
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

        /// <summary>
        /// The HTTP response message.
        /// </summary>
        public HttpMethod Method { get; }

        /// <summary>
        /// The URI that was requested.
        /// </summary>
        public string RequestUri { get; }

        /// <summary>
        /// The response status code.
        /// </summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>
        /// The response reason phrase.
        /// </summary>
        public string ReasonPhrase { get; }
    }
}
