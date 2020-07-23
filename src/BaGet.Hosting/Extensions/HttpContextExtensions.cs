using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace BaGet.Hosting
{
    internal static class HttpContextExtensions
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            IgnoreNullValues = true,
        };

        public static string ReadFromQuery(this HttpRequest request, string key)
        {
            var value = request.Query[key].ToString();
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value;
        }

        public static string ReadFromQuery(this HttpRequest request, string key, string defaultValue)
        {
            var value = request.Query[key].ToString();
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            return value;
        }

        public static int ReadFromQuery(this HttpRequest context, string key, int defaultValue)
        {
            var value = context.Query[key].ToString();
            if (value == null || !int.TryParse(value, out var result))
            {
                result = defaultValue;
            }

            return result;
        }

        public static bool ReadFromQuery(this HttpRequest request, string key, bool defaultValue)
        {
            var value = request.Query[key].ToString();
            if (value == null || !bool.TryParse(value, out var result))
            {
                result = defaultValue;
            }

            return result;
        }

        public static void NotFound(this HttpResponse response) => response.StatusCode = StatusCodes.Status404NotFound;

        public static async Task WriteAsJsonAsync<TValue>(
            this HttpResponse response,
            TValue value,
            CancellationToken cancellationToken)
        {
            response.ContentType = "application/json; charset=utf-8";

            await JsonSerializer.SerializeAsync(response.Body, value, JsonOptions, cancellationToken);
        }
    }
}
