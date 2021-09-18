using System;
using System.IO;
using Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BaGet
{
    public class ConfigureRazorRuntimeCompilation : IConfigureOptions<MvcRazorRuntimeCompilationOptions>
    {
        private readonly IHostEnvironment _env;

        public ConfigureRazorRuntimeCompilation(IHostEnvironment env)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        public void Configure(MvcRazorRuntimeCompilationOptions options)
        {
            var path = Path.Combine(_env.ContentRootPath, "..", "BaGet.Web");

            // Try to enable Razor "hot reload".
            if (!_env.IsDevelopment()) return;
            if (!Directory.Exists(path)) return;

            var provider = new PhysicalFileProvider(Path.GetFullPath(path));

            options.FileProviders.Add(provider);
        }
    }
}
