using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using BaGet.Core;
using BaGet.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SpaServices;
using Microsoft.AspNetCore.SpaServices.ReactDevelopmentServer;
using Microsoft.AspNetCore.SpaServices.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace BaGet
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.ConfigureHttpServices();

            // In production, the UI files will be served from this directory
            services.AddSpaStaticFiles(configuration =>
            {
                configuration.RootPath = "BaGet.UI/build";
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var options = Configuration.Get<BaGetOptions>();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseStatusCodePages();
            }

            app.UseForwardedHeaders();
            app.UsePathBase(options.PathBase);

            app.UseSpaStaticFiles();

            app.UseRouting();

            app.UseCors(ConfigureCorsOptions.CorsPolicy);
            app.UseOperationCancelledMiddleware();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapServiceIndexRoutes();
                endpoints.MapPackagePublishRoutes();
                endpoints.MapSymbolRoutes();
                endpoints.MapSearchRoutes();
                endpoints.MapPackageMetadataRoutes();
                endpoints.MapPackageContentRoutes();
            });

            app.UseSpa(spa =>
            {
                spa.Options.SourcePath = "../BaGet.UI";

                if (env.IsDevelopment())
                {
                    spa.UseReactDevelopmentServer(npmScript: "start");
                }
                else
                {
                    spa.UseBaGetFileProvider();
                }
            });
        }
    }

    public static class SpaExtensions
    {
        public static void UseBaGetFileProvider(this ISpaBuilder spa)
        {
            var services = spa.ApplicationBuilder.ApplicationServices;
            var options = services.GetRequiredService<IOptions<BaGetOptions>>();
            var defaultSpaFiles = services.GetRequiredService<ISpaStaticFileProvider>();

            spa.Options.DefaultPageStaticFileOptions = new StaticFileOptions
            {
                FileProvider = new BaGetSpaFileProvider(defaultSpaFiles.FileProvider, options)
            };
        }
    }

    public class BaGetSpaFileProvider : IFileProvider
    {
        private readonly IFileProvider _fileProvider;
        private readonly IOptions<BaGetOptions> _options;

        private readonly ConcurrentDictionary<string, IFileInfo> _cache;

        public BaGetSpaFileProvider(IFileProvider fileProvider, IOptions<BaGetOptions> options)
        {
            _fileProvider = fileProvider;
            _options = options;

            _cache = new ConcurrentDictionary<string, IFileInfo>();
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            return _fileProvider.GetDirectoryContents(subpath);
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            var rawFile = _fileProvider.GetFileInfo(subpath);
            var pathBase = _options.Value.PathBase;

            if (!rawFile.Exists || rawFile.PhysicalPath == null)
            {
                return rawFile;
            }

            // Don't do anything fancy if we don't need to replace the path base.
            if (string.IsNullOrEmpty(pathBase))
            {
                return rawFile;
            }

            // Only JavaScript and HTML files need replacements.
            var extension = Path.GetExtension(subpath);
            if (!extension.Equals(".js", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".html", StringComparison.OrdinalIgnoreCase))
            {
                return rawFile;
            }

            // Use a cached file info if possible.
            if (_cache.TryGetValue(subpath, out var cachedFileInfo) &&
                cachedFileInfo.LastModified >= rawFile.LastModified)
            {
                return cachedFileInfo;
            }

            // Build a new file and add it to the cache.
            var newFile = BuildFileInfo(rawFile);

            return _cache.AddOrUpdate(subpath, newFile, (_, cached) =>
            {
                return (cached.LastModified >= newFile.LastModified)
                    ? cached
                    : newFile;
            });
        }

        private IFileInfo BuildFileInfo(IFileInfo rawFile)
        {
            // Don't do anything if the file doens't have any placeholders.
            var rawContent = File.ReadAllText(rawFile.PhysicalPath);
            if (!rawContent.Contains("__BAGET_PLACEHOLDER"))
            {
                return rawFile;
            }

            var replacedContent = Encoding.UTF8.GetBytes(
                rawContent
                    .Replace("__BAGET_PLACEHOLDER_API_URL__", "TODO")
                    .Replace("__BAGET_PLACEHOLDER_PATH_BASE__", _options.Value.PathBase));

            return new MemoryFileInfo(rawFile, replacedContent);
        }

        public IChangeToken Watch(string filter)
        {
            return _fileProvider.Watch(filter);
        }

        private class MemoryFileInfo : IFileInfo
        {
            private readonly byte[] _content;

            public MemoryFileInfo(IFileInfo file, byte[] content)
            {
                _content = content;

                Length = content.Length;
                Name = file.Name;
                LastModified = file.LastModified;
            }

            public bool Exists => true;
            public long Length { get; }
            public string Name { get; }
            public DateTimeOffset LastModified { get; }
            public bool IsDirectory => false;

            // Prevent ASP.NET Core from loading the file from disk
            public string PhysicalPath => null;

            public Stream CreateReadStream()
            {
                return new MemoryStream(_content, writable: false);
            }
        }
    }
}
