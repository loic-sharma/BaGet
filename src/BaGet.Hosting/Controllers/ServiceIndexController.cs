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
  ""allRepositorySigned"": true,
  ""signingCertificates"": [
    {
      ""fingerprints"": {
        ""2.16.840.1.101.3.4.2.1"": ""0e5f38f57dc1bcc806d8494f4f90fbcedd988b46760709cbeec6f4219aa6157d""
      },
      ""subject"": ""CN=NuGet.org Repository by Microsoft, O=NuGet.org Repository by Microsoft, L=Redmond, S=Washington, C=US"",
      ""issuer"": ""CN=DigiCert SHA2 Assured ID Code Signing CA, OU=www.digicert.com, O=DigiCert Inc, C=US"",
      ""notBefore"": ""2018-04-10T00:00:00.0000000Z"",
      ""notAfter"": ""2021-04-14T12:00:00.0000000Z"",
      ""contentUrl"": ""https://api.nuget.org/v3-index/repository-signatures/certificates/0e5f38f57dc1bcc806d8494f4f90fbcedd988b46760709cbeec6f4219aa6157d.crt""
    }
  ]
}";

            return Content(content, "application/json");
        }
    }
}
