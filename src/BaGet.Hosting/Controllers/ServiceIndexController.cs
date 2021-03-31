using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Protocol.Models;
using Microsoft.AspNetCore.Mvc;

namespace BaGet.Hosting
{
    /// <summary>
    /// The NuGet Service Index. This aids NuGet client to discover this server's services.
    /// </summary>
    public class ServiceIndexController : Controller
    {
        private readonly IServiceIndexService _serviceIndex;

        public ServiceIndexController(IServiceIndexService serviceIndex)
        {
            _serviceIndex = serviceIndex ?? throw new ArgumentNullException(nameof(serviceIndex));
        }

        // GET v3/index
        [HttpGet]
        public async Task<ServiceIndexResponse> GetAsync(CancellationToken cancellationToken)
        {
            return await _serviceIndex.GetAsync(cancellationToken);
        }

        // GET v3/repository-signatures/5.0.0/index.json
        [HttpGet]
        public IActionResult RepositorySignatures()
        {
            var content = @"
{
  ""fingerprints"": {
    ""2.16.840.1.101.3.4.2.1"": ""4c1e35171b864f1a3a4b472def336259286fd8596f32b4dc4c0212e69caa3388""
  },
  ""subject"": ""CN=Test certificate for testing NuGet package signing"",
  ""issuer"": ""CN=Test certificate for testing NuGet package signing"",
  ""notBefore"": ""2021-03-31T21:29:11.0000000Z"",
  ""notAfter"": ""2021-03-31T23:30:00.0000000Z"",
  ""contentUrl"": ""https://loshardev0.blob.core.windows.net/api/593DC447F9EC0A334B88D455B1857D311B9C3965.crt""
}";

            return Content(content, "application/json");
        }
    }
}
