using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BaGet.Core.Server.Transformers
{
    public class NugetPackageMetadataRoutesTransformer : DynamicRouteValueTransformer
    {
        public NugetPackageMetadataRoutesTransformer()
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
                values["controller"] = "PackageMetadata";
                values["action"] = "RegistrationIndex";
            }

            return values;
        }
    }
}
