using System;
using System.IO;
using BaGet.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet;

/// <summary>The data protection key ring of a single node.</summary>
internal static class HostDataProtectionExtensions
{
    /// <summary>The folder name used next to the settings overrides file when <c>DataProtection:KeyPath</c> is not set.</summary>
    public const string DefaultFolderName = "DataProtection-Keys";

    /// <summary>
    /// Persist the key ring to a folder (<c>DataProtection:KeyPath</c>, default <see cref="DefaultFolderName"/> next to the overrides
    /// file), so a recreated container or an IIS pool without a user profile keeps the keys that protect stored secrets, sessions and
    /// tokens. Does nothing in cluster mode, where <c>AddBaGetHa</c> keeps the ring in the database. The application name is left at its
    /// default on purpose: changing it would change the purpose of everything already protected.
    /// </summary>
    public static IServiceCollection AddBaGetDataProtection(this IServiceCollection services, IConfiguration configuration, string overridesPath)
    {
        var ha = configuration.GetSection(nameof(BaGetOptions.Ha)).Get<HaOptions>();
        if (ha is { Enabled: true }) return services;

        var keyPath = configuration.GetSection(nameof(BaGetOptions.DataProtection)).Get<DataProtectionKeyOptions>()?.KeyPath;
        var folder = ResolveKeyFolder(keyPath, overridesPath);

        EnsureWritable(folder);

        var builder = services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(folder));

        // On Windows the framework default encrypted the keys at rest with DPAPI; keep that (machine scope: an IIS pool identity may have no
        // loaded user profile). Elsewhere the keys are plain XML, so the folder must be protected by the file system (see docs/configuration.md).
        if (OperatingSystem.IsWindows()) builder.ProtectKeysWithDpapi(protectToLocalMachine: true);

        return services;
    }

    /// <summary>Stops startup with a clear message when the key folder cannot be created or written, instead of failing every protect call later.</summary>
    internal static void EnsureWritable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The data protection key folder '{folder}' cannot be written ({e.Message}). Grant the application write access to it or set DataProtection:KeyPath to a writable folder.", e);
        }
    }

    /// <summary>The absolute key folder: <paramref name="keyPath"/> (relative to the overrides file's folder), else the default beside the file.</summary>
    internal static string ResolveKeyFolder(string? keyPath, string overridesPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(overridesPath)) ?? Directory.GetCurrentDirectory();

        return string.IsNullOrWhiteSpace(keyPath)
            ? Path.Combine(directory, DefaultFolderName)
            : Path.GetFullPath(keyPath.Trim(), directory);
    }
}
