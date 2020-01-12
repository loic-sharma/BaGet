using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BaGet.Core.Server.Transformers
{
    public class NugetSearchRoutesTransformer : DynamicRouteValueTransformer
    {
        public NugetSearchRoutesTransformer()
        {
        }

        public override ValueTask<RouteValueDictionary> TransformAsync(HttpContext httpContext, RouteValueDictionary values)
        {
            return new ValueTask<RouteValueDictionary>(new RouteValueDictionary(SetValues(httpContext, values)));
        }

        private RouteValueDictionary SetValues(HttpContext httpContext, RouteValueDictionary values)
        {
            values["controller"] = "Search";

            return values;
        }
    }
}
