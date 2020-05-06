using System;
using System.IO;
using BaGet.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SpaServices.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Hosting
{
    public class BaGetSpaStaticFileProvider : ISpaStaticFileProvider
    {
        public BaGetSpaStaticFileProvider(IWebHostEnvironment environment, IOptions<BaGetOptions> options, ILogger<BaGetSpaStaticFileProvider> logger)
        {
            var rootPath = "BaGet.UI/build";
            if (environment.IsDevelopment())
            {
                rootPath = "../" + rootPath;
            }

            var combinedRootPath = new DirectoryInfo(Path.Combine(environment.ContentRootPath, rootPath));
            if (combinedRootPath.Exists)
            {
                var pathBase = options.Value.PathBase;
                if (string.IsNullOrWhiteSpace(pathBase) || pathBase.Trim().Equals("/"))
                {
                    pathBase = "";
                }
                else if (!pathBase.StartsWith("/"))
                {
                    pathBase = "/" + pathBase.Trim().TrimEnd('/');
                }

                FileProvider = new BaGetUIFileProvider(combinedRootPath, pathBase);
            }
            else
            {
                FileProvider = new NullFileProvider();

                if (environment.IsDevelopment())
                {
                    logger.LogWarning("It seems like you are in Development environment.\nIf you want to Test with static files please adjust the root path e.g. ../BaGet.UI/build");
                }
                else
                {
                    logger.LogError($"Please verify that {combinedRootPath} exists.");
                    logger.LogError("UI Files not found. This results in a not working UI");
                }
            }
        }

        public IFileProvider FileProvider { get; }
    }
}
