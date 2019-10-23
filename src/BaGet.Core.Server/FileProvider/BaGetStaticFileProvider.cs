using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.SpaServices.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace BaGet.Core.Server.FileProvider
{
    public class BaGetStaticFileProvider : ISpaStaticFileProvider
    {

        private readonly IServiceProvider _serviceProvider;
        private ILogger<BaGetStaticFileProvider> _logger;
        private IFileProvider _fileProvider;
        public BaGetStaticFileProvider(IServiceProvider serviceProvider, string RootPath, ILogger<BaGetStaticFileProvider> logger)
        {
            _serviceProvider = serviceProvider;
            ConfigureRootPath(RootPath);
            _logger = logger;
        }

        public void ConfigureRootPath(string RootPath)
        {
            var env = _serviceProvider.GetRequiredService<IHostingEnvironment>();
            var combinedRootPath = Path.Combine(env.ContentRootPath, RootPath);
            var bagetOptions = _serviceProvider.GetRequiredService<IOptions<BaGetOptions>>();
            try
            {
                _fileProvider = new BaGetUIFileProvider(combinedRootPath, bagetOptions.Value);
            }catch(DirectoryNotFoundException e)
            {
                if (env.IsDevelopment())
                {
                    _logger.LogWarning("It seems like you are in Development environment.\nIf you want to Test with static files please adjust the root path e.g. ../BaGet.UI/build");
                }
                else
                {
                    _logger.LogError($"Please verify that {combinedRootPath} exists.");
                    throw e;
                }
            }
        }
        public IFileProvider FileProvider => _fileProvider;
    }
}
