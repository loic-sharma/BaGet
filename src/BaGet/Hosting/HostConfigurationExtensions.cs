using System;
using System.IO;
using System.Linq;
using BaGet.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;

namespace BaGet;

/// <summary>The host's configuration sources.</summary>
internal static class HostConfigurationExtensions
{
    /// <summary>
    /// Apply BAGET_CONFIG_ROOT and add the Settings page's overrides file as a configuration
    /// source. It sits right after the last appsettings*.json source, so it beats those
    /// files but not user secrets, environment variables or the command line: an operator's
    /// explicit environment still wins.
    /// </summary>
    /// <returns>The overrides file's absolute path.</returns>
    public static string AddBaGetSettingsOverrides(this ConfigurationManager configuration, IWebHostEnvironment environment)
    {
        var root = Environment.GetEnvironmentVariable("BAGET_CONFIG_ROOT");

        if (!string.IsNullOrEmpty(root))
        {
            configuration.SetBasePath(root);
        }

        var path = SettingsOverridesStore.ResolvePath(
            configuration,
            string.IsNullOrEmpty(root) ? environment.ContentRootPath : root);
        var directory = Path.GetDirectoryName(path);

        try
        {
            #pragma warning disable CS8604
            Directory.CreateDirectory(directory);
            #pragma warning restore CS8604
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A read-only configuration root: the page then reports the error when saving.
        }

        // A file provider needs an existing directory; without one there is nothing to load,
        // and the host must still start.
        if (!Directory.Exists(directory)) return path;

        var source = new JsonConfigurationSource
        {
            Path = Path.GetFileName(path),
            Optional = true,
            ReloadOnChange = true,
            FileProvider = new PhysicalFileProvider(directory, ExclusionFilters.None),
        };

        // After the last appsettings*.json, not after the last JSON source: user secrets (Development) are a JSON source too and
        // would otherwise end up below the overrides file, which then outranks them.
        var index = configuration.Sources.ToList().FindLastIndex(IsAppSettingsSource) + 1;
        configuration.Sources.Insert(index, source);

        return path;
    }

    private static bool IsAppSettingsSource(IConfigurationSource source)
        => source is JsonConfigurationSource json
            && Path.GetFileName(json.Path ?? string.Empty).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase);
}
