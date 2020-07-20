using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    internal static class HttpClientExtensions
    {
        /// <summary>
        /// Deserialize JSON content. If the HTTP response status code is 404,
        /// returns the default value.
        /// </summary>
        /// <typeparam name="TResult">The JSON type to deserialize.</typeparam>
        /// <param name="httpClient">The HTTP client that will perform the request.</param>
        /// <param name="requestUri">The request URI.</param>
        /// <param name="cancellationToken">A token to cancel the task.</param>
        /// <returns>The JSON content, or the default value if the HTTP response status code is 404.</returns>
        public static async Task<TResult> GetFromJsonOrDefaultAsync<TResult>(
            this HttpClient httpClient,
            string requestUri,
            CancellationToken cancellationToken = default)
        {
            using (var response = await httpClient.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return default;
                }

                return await response.Content.ReadFromJsonAsync<TResult>();
            }
        }
    }
}
