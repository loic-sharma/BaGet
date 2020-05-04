using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace BaGet.Tests
{
    /// <summary>
    /// Similar to official HttpSourceResourceProvider, but uses test host.
    /// </summary>
    public class HttpSourceResourceProviderTestHost : ResourceProvider
    {
        // Only one HttpSource per source should exist. This is to reduce the number of TCP connections.
        private readonly ConcurrentDictionary<PackageSource, HttpSourceResource> _cache
            = new ConcurrentDictionary<PackageSource, HttpSourceResource>();
        private readonly HttpClient _httpClient;

        public HttpSourceResourceProviderTestHost(HttpClient httpClient)
            : base(typeof(HttpSourceResource),
                  nameof(HttpSourceResource),
                  NuGetResourceProviderPositions.Last)
        {
            _httpClient = httpClient;
        }

        public override Task<Tuple<bool, INuGetResource>> TryCreate(SourceRepository source, CancellationToken token)
        {
            Debug.Assert(source.PackageSource.IsHttp, "HTTP source requested for a non-http source.");

            HttpSourceResource curResource = null;

            if (source.PackageSource.IsHttp)
            {
                curResource = _cache.GetOrAdd(
                    source.PackageSource, 
                    packageSource => new HttpSourceResource(TestableHttpSource.Create(source, _httpClient)));
            }

            return Task.FromResult(new Tuple<bool, INuGetResource>(curResource != null, curResource));
        }
    }
}
