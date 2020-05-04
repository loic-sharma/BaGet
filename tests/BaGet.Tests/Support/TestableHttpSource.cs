using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace BaGet.Tests
{
    public class TestableHttpSource : HttpSource
    {
        public static TestableHttpSource Create(SourceRepository source, HttpClient client)
        {
            Func<Task<HttpHandlerResource>> factory = () => source.GetResourceAsync<HttpHandlerResource>();

            var httpCacheDirectory = Path.Combine(
                Path.GetTempPath(),
                "BaGetTests",
                Guid.NewGuid().ToString("N"));

            return new TestableHttpSource(
                source.PackageSource,
                factory,
                NullThrottle.Instance,
                client,
                httpCacheDirectory);
        }

        private TestableHttpSource(
            PackageSource packageSource,
            Func<Task<HttpHandlerResource>> messageHandlerFactory,
            IThrottle throttle,
            HttpClient httpClient,
            string httpCacheDirectory)
            : base(packageSource, messageHandlerFactory, throttle)
        {
            // Set the HTTP client on the parent's private field.
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var field = typeof(HttpSource).GetField("_httpClient", flags);

            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Could not find field '_httpClient' with flags '{flags}' " +
                    $"on {nameof(HttpSource)}");
            }

            field.SetValue(this, httpClient);

            HttpCacheDirectory = httpCacheDirectory;
        }
    }
}
