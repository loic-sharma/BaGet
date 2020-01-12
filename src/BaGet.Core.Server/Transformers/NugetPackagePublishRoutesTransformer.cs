using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BaGet.Core.Server.Transformers
{
    public class NugetPackagePublishRoutesTransformer : DynamicRouteValueTransformer
    {
        public NugetPackagePublishRoutesTransformer()
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
                values["controller"] = "PackagePublish";
                if (HttpMethods.IsPut(httpContext.Request.Method))
                {
                    values["action"] = "Upload";
                }
                else if (HttpMethods.IsDelete(httpContext.Request.Method))
                {
                    values["action"] = "Delete";
                }
                else if (HttpMethods.IsPost(httpContext.Request.Method))
                {
                    values["action"] = "Relist";
                }
            }

            return values;
        }
    }
}
