using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BaGet.Protocol
{
    internal static class HttpClientExtensions
    {
        internal static readonly JsonSerializer Serializer = JsonSerializer.Create(JsonSettings);

        internal static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateParseHandling = DateParseHandling.DateTimeOffset,
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static async Task<ResponseAndResult<T>> DeserializeUrlAsync<T>(
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
                    return new ResponseAndResult<T>(
                        HttpMethod.Get,
                        documentUrl,
                        response.StatusCode,
                        response.ReasonPhrase,
                        hasResult: false,
                        result: default);
                }

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var textReader = new StreamReader(stream))
                using (var jsonReader = new JsonTextReader(textReader))
                {
                    return new ResponseAndResult<T>(
                        HttpMethod.Get,
                        documentUrl,
                        response.StatusCode,
                        response.ReasonPhrase,
                        hasResult: true,
                        result: Serializer.Deserialize<T>(jsonReader));
                }
            }
        }
    }
}
