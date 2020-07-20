using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    internal static class HttpClientExtensions
    {
        public static async Task<ResponseAndResult<TResult>> DeserializeUrlAsync<TResult>(
            this HttpClient httpClient,
            string documentUrl,
            CancellationToken cancellationToken = default)
        {
            using (var response = await httpClient.GetAsync(
                documentUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken))
            {
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return new ResponseAndResult<TResult>(
                        HttpMethod.Get,
                        documentUrl,
                        response.StatusCode,
                        response.ReasonPhrase,
                        hasResult: false,
                        result: default);
                }

                var result = await response.Content.ReadFromJsonAsync<TResult>();

                return new ResponseAndResult<TResult>(
                    HttpMethod.Get,
                    documentUrl,
                    response.StatusCode,
                    response.ReasonPhrase,
                    hasResult: true,
                    result: result);
            }
        }
    }
}
