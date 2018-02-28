using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NuGet.Packaging;

namespace BaGet.Controllers
{
    public class PackagePublishController : Controller
    {
        public string Upload(IFormFile upload)
        {
            using (var uploadStream = upload.OpenReadStream())
            using (var package = new PackageArchiveReader(uploadStream))
            {
                HttpContext.Response.StatusCode = 201;

                var nuspec = package.NuspecReader;

                return $"{nuspec.GetId()}@{nuspec.GetVersion()}";
            }
        }

        public void Delete(string id, string version)
        {
            HttpContext.Response.StatusCode = 404;
        }

        public void Relist(string id, string version)
        {
            HttpContext.Response.StatusCode = 404;
        }
    }
}
