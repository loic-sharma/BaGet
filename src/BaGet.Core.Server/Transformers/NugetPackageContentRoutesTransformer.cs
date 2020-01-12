using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BaGet.Core.Server.Transformers
{
    public class NugetPackageContentRoutesTransformer : DynamicRouteValueTransformer
    {
        public NugetPackageContentRoutesTransformer()
        {
        }

        public override ValueTask<RouteValueDictionary> TransformAsync(HttpContext httpContext, RouteValueDictionary values)
        {
            return new ValueTask<RouteValueDictionary>(new RouteValueDictionary(SetValues(httpContext, values)));
        }

        private RouteValueDictionary SetValues(HttpContext httpContext, RouteValueDictionary values)
        {
            if (values is RouteValueDictionary)
            {
                values["controller"] = "PackageContent";
                values["action"] = "GetPackageVersions";
            }

            return values;
        }
    }
}
