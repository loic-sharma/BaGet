using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using BaGet.Core.Services;
using BaGet.Legacy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using NuGet.Versioning;

namespace BaGet.Controllers
{
    public class PackagesV2Controller : Controller
    {
        static readonly string atomXmlContentType = "application/atom+xml";
        static readonly string basePath = "/v2";

        private readonly IODataPackageSerializer _serializer;
        private readonly IPackageService _packageService;
        private readonly IPackageStorageService _storage;
        private readonly IEdmModel _odataModel;
        private readonly ILogger<PackagesV2Controller> _log;

        public PackagesV2Controller(
            IODataPackageSerializer serializer,
            IPackageService packageService,
            IPackageStorageService storage,
            IEdmModel odataModel,
            ILogger<PackagesV2Controller> logger)
        {
            _serializer = serializer;
            _packageService = packageService;
            _storage = storage;
            _odataModel = odataModel;
            _log = logger;
        }

        public async Task<IActionResult> DownloadPackage(string id, string version)
        {
            if (!NuGetVersion.TryParse(version, out var nugetVersion))
            {
                return BadRequest();
            }

            if (!await _packageService.AddDownloadAsync(id, nugetVersion))
            {
                return BadRequest();
            }

            var packageStream = await _storage.GetPackageStreamAsync(id, nugetVersion, CancellationToken.None);
            return File(packageStream, "application/octet-stream");
        }

        [HttpGet]
        public async Task Index()
        {
            var serviceUrl = GetServiceUrl(Request);
            var text = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<service xml:base=""{serviceUrl}"" xmlns=""http://www.w3.org/2007/app"" xmlns:atom=""http://www.w3.org/2005/Atom""><workspace>
<atom:title type=""text"">Default</atom:title><collection href=""Packages""><atom:title type=""text"">Packages</atom:title></collection></workspace>
</service>";
            Response.StatusCode = 200;
            Response.ContentType = "application/xml; charset=utf-8";
            await Response.WriteAsync(text, new UTF8Encoding(false));
        }

        public async Task<IActionResult> FindPackagesById()
        {
            try
            {
                var serviceUrl = GetServiceUrl(Request);
                var uriParser = new ODataUriParser(_odataModel, new Uri(serviceUrl), GetUri(Request));
                var path = uriParser.ParsePath();
                if (path.FirstSegment.Identifier == "FindPackagesById")
                {
                    var idOrNull = uriParser.CustomQueryOptions.FirstOrDefault(o => o.Key.ToLowerInvariant() == "id").Value;
                    var id = idOrNull.TrimStart('\'').TrimEnd('\'');
                    _log.LogDebug("Request to FindPackagesById id={0}", id);
                    var found = await _packageService.FindAsync(id);
                    var odata = new ODataResponse<IEnumerable<PackageWithUrls>>(serviceUrl, found.Select(f => ToPackageWithUrls(Request, f)));
                    var ms = new MemoryStream();
                    _serializer.Serialize(ms, odata.Entity, odata.ServiceBaseUrl);
                    ms.Seek(0, SeekOrigin.Begin);
                    return File(ms, atomXmlContentType);
                }
                return BadRequest();
            }
            catch (ODataException odataPathError)
            {
                _log.LogError("Bad odata query", odataPathError);
                return BadRequest();
            }
        }

        public async Task<IActionResult> QueryPackages()
        {
            try
            {
                var serviceUrl = GetServiceUrl(Request);
                var uri = GetUri(Request);
                var uriParser = new ODataUriParser(_odataModel, new Uri(serviceUrl), uri);
                var path = uriParser.ParsePath();
                if (path.FirstSegment.Identifier == "Packages")
                {
                    if (path.Count == 2 && path.LastSegment is KeySegment)
                    {
                        KeySegment queryParams = (KeySegment)path.LastSegment;
                        var id = queryParams.Keys.First(k => k.Key == "Id").Value as string;
                        var version = queryParams.Keys.First(k => k.Key == "Version").Value as string;
                        _log.LogDebug("Request to find package by id={0} and version={1}", id, version);
                        var nugetVersion = NuGetVersion.Parse(version);
                        var found = await _packageService.FindOrNullAsync(id, nugetVersion);
                        if (found == null)
                        {
                            return NotFound();
                        }
                        return ToODataStream(found);
                    }
                    else // might be Chocolatey, uses url like: /v2/Packages()?$filter=tolower(Id) eq 'package-id'&$orderby=Id  
                    {
                        var filter = uriParser.ParseFilter().Expression;
                        var binary = (BinaryOperatorNode)filter;
                        var leftParam = ((SingleValueFunctionCallNode)binary.Left).Parameters.First();
                        var convert = (ConvertNode)leftParam;
                        var prop = (SingleValuePropertyAccessNode)convert.Source;
                        if (prop.Property.Name == "Id")
                        {
                            var id = ((ConstantNode)binary.Right).Value.ToString();
                            var packages = await _packageService.FindAsync(id);
                            var found = packages.OrderBy(x => x.Version).LastOrDefault();
                            if (found == null)
                            {
                                return NotFound();
                            }
                            return ToODataStream(found);
                        }
                    }

                    // rest of OData support would go here
                }
            }
            catch (ODataException odataPathError)
            {
                _log.LogError("Bad odata query", odataPathError);
                return BadRequest();
            }
            return BadRequest();
        }

        private FileStreamResult ToODataStream(Package package)
        {
            var odataPackage = new ODataResponse<PackageWithUrls>(GetServiceUrl(Request), ToPackageWithUrls(Request, package));
            var ms = new MemoryStream();
            _serializer.Serialize(
                ms,
                odataPackage.Entity.Pkg,
                odataPackage.ServiceBaseUrl,
                odataPackage.Entity.ResourceIdUrl,
                odataPackage.Entity.PackageContentUrl);
            ms.Seek(0, SeekOrigin.Begin);
            return File(ms, atomXmlContentType);
        }

        private PackageWithUrls ToPackageWithUrls(HttpRequest request, Package pkg)
        {
            var serviceUrl = GetServiceUrl(request);
            var id = pkg.Id;
            var version = pkg.Version;
            PackageWithUrls urls = new PackageWithUrls(pkg,
                $"{serviceUrl}/Packages(Id='{id}',Version='{pkg.Version}')",
                $"{serviceUrl}/contents/{id.ToLowerInvariant()}/{version.ToNormalizedString()}");
            return urls;
        }

        private string GetServiceUrl(HttpRequest request)
        {
            var builder = new UriBuilder();
            builder.Scheme = request.Scheme;
            builder.Host = request.Host.Host;
            builder.Port = request.Host.Port ?? 80;
            builder.Path = basePath;
            return builder.Uri.ToString();
        }

        public static Uri GetUri(HttpRequest request)
        {
            var builder = new UriBuilder();
            builder.Scheme = request.Scheme;
            builder.Host = request.Host.Host;
            builder.Port = request.Host.Port ?? 80;
            builder.Path = request.Path;
            builder.Query = request.QueryString.ToUriComponent();
            return builder.Uri;
        }
    }
}
