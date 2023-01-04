using System;
using System.Collections.Generic;
using System.Net.Http;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;

namespace BaGet.Tests
{
    public static class TestableSourceRepository
    {
        public static SourceRepository Build(Uri source, HttpClient client)
        {
            var packageSource = new PackageSource(source.AbsoluteUri);
            var providers = new List<Lazy<INuGetResourceProvider>>();

            var testableHttpProvider = new Lazy<INuGetResourceProvider>(
                () => new TestableHttpSourceResourceProvider(client));

            providers.Add(testableHttpProvider);
            providers.AddRange(Repository.Provider.GetCoreV3());

            return new SourceRepository(packageSource, providers);
        }
    }
}
