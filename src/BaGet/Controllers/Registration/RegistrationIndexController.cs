using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Services;
using BaGet.Extensions;
using Microsoft.AspNetCore.Mvc;
using NuGet.Versioning;

namespace BaGet.Controllers.Registration
{
    /// <summary>
    /// The API to retrieve the metadata of a specific package.
    /// </summary>
    public class RegistrationIndexController : Controller, IUrlExtension
    {
        private readonly IRegistrationIndexService _registrationIndexService;

        public RegistrationIndexController(IRegistrationIndexService registrationIndexService)
        {
            _registrationIndexService = registrationIndexService ?? throw new ArgumentNullException(nameof(registrationIndexService));
        }

        // GET v3/registration/{id}.json
        [HttpGet]
        public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
        {
            var index = await _registrationIndexService.Get(id, this, cancellationToken);

            if (index == null)
            {
                return NotFound();
            }
            return Json(index);
        }

        string IUrlExtension.PackageDownload(string id, NuGetVersion version)
        {
            return Url.PackageDownload(id, version);
        }

        string IUrlExtension.PackageRegistration(string id)
        {
            return Url.PackageRegistration(id);
        }

        string IUrlExtension.PackageRegistration(string id, NuGetVersion version)
        {
            return Url.PackageRegistration(id, version);
        }
    }
}
