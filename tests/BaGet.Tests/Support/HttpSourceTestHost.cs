// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace BaGet.Tests.Support
{
    public class HttpSourceTestHost : HttpSource
    {
        public static HttpSourceTestHost Create(SourceRepository source, HttpClient client)
        {
            Func<Task<HttpHandlerResource>> factory = () => source.GetResourceAsync<HttpHandlerResource>();

            return new HttpSourceTestHost(source.PackageSource, factory, NullThrottle.Instance, client);
        }
        public HttpSourceTestHost(
            PackageSource packageSource,
            Func<Task<HttpHandlerResource>> messageHandlerFactory,
            IThrottle throttle,
            HttpClient client)
            : base(packageSource, messageHandlerFactory, throttle)
        {
            var prop = typeof(HttpSource).GetField("_httpClient",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            prop.SetValue(this, client);
            HttpCacheDirectory = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
            var cacheDir = new DirectoryInfo(HttpCacheDirectory);
            if (cacheDir.Exists)
            {
                foreach (var dir in cacheDir.EnumerateDirectories())
                {
                    dir.Delete(true);
                }
            }
        }
    }
}
