using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Protocol
{
    internal static class HttpClientExtensions
    {
        internal static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IgnoreNullValues = true,
        };

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

                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var result = await JsonSerializer.DeserializeAsync<TResult>(stream, Options, cancellationToken);

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
}
