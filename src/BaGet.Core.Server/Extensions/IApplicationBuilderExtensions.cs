using System;
using System.IO;
using Microsoft.AspNetCore.Builder;

namespace BaGet.Core.Server.Extensions
{
    public static class IApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseOperationCancelledMiddleware(this IApplicationBuilder app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            return app.UseMiddleware<OperationCancelledMiddleware>();
        }

        public static IApplicationBuilder UsePathBase(this IApplicationBuilder app, BaGetOptions options, string SpaRootPath)
        {
            
            if (app == null) throw new ArgumentNullException(nameof(app));
            var pathBase = options.PathBase;
            app.UsePathBase(pathBase);
            if(string.IsNullOrWhiteSpace(pathBase) || pathBase.Trim().Equals("/"))
            {
                pathBase = "";
            } else
            {
                if(!pathBase.StartsWith("/"))
                {
                    pathBase = "/" + pathBase.Trim().TrimEnd('/');
                }
            }
            SpaRootPath = SpaRootPath.TrimEnd('/');

            Console.WriteLine("BaGet is configured to be hosted under: " + (string.IsNullOrEmpty(pathBase) ? "/" : pathBase));
            if (Directory.Exists(SpaRootPath))
            {
                var indexContent = File.ReadAllText(SpaRootPath + "/index.html");
                indexContent = indexContent.Replace("__BAGET_PATH_BASE_PLACEHOLDER__", pathBase);
                File.WriteAllText(SpaRootPath + "/index.html", indexContent);
                string[] files = Directory.GetFiles(SpaRootPath + "/static/js/");

                foreach (var file in files)
                {
                    var content = File.ReadAllText(file);
                    //__BAGET_PLACEHOLDER_API_URL__
                    content = content.Replace("__BAGET_PATH_BASE_PLACEHOLDER__", pathBase).Replace("__BAGET_PLACEHOLDER_API_URL__", pathBase);
                    File.WriteAllText(file, content);
                }
            }
            else
            {
                Console.WriteLine("Could not find React files");
            }
            return app;
        }
    }
}
