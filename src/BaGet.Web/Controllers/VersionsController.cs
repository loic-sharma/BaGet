using BaGet.Core;
using BaGet.Web.Dtos;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace BaGet.Web.Controllers
{
    [Route("v3-flatcontainer/{packageId}/index.json")]
    [ApiController]
    public class VersionsController : ControllerBase
    {
        private readonly IPackageDatabase _packageDatabase;
        public VersionsController(IPackageDatabase packageDatabase)
        {
            _packageDatabase = packageDatabase;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<string>>> GetVersionsForPackage(string packageId)
        {
            Console.WriteLine($"--> Getting versions for package {packageId} from Automaise nuget");
            var exists = await _packageDatabase.ExistsAsync(packageId, new System.Threading.CancellationToken());
            if (!exists)
                return NotFound();
            var retVersions = await _packageDatabase.FindAsync(packageId, true, new System.Threading.CancellationToken());
            var versionDto = new VersionDto();
            versionDto.versions = retVersions.Select(x => x.Version.ToString()).ToList();
            return Ok(versionDto);
        }
    }
}
