using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BaGet.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The Settings catalog is built from the options types the real application registers, so
/// these tests run against the real host: a new options type or property that is missing a
/// descriptor, or a credential that is not marked sensitive, fails here.
/// </summary>
public class SettingsCatalogTests : IDisposable
{
    private static readonly Regex CredentialName =
        new(@"password|secret|apikey|connectionstring|accesskey|accountkey|credential", RegexOptions.IgnoreCase);

    private readonly BaGetApplication _app;
    private readonly ITestOutputHelper _output;

    public SettingsCatalogTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new BaGetApplication(output);
        _ = _app.Server;
    }

    public void Dispose() => _app.Dispose();

    private SettingsCatalog Catalog => _app.Services.GetRequiredService<SettingsCatalog>();

    private IEnumerable<SettingsSchemaRegistration> Registrations => _app.Services.GetServices<SettingsSchemaRegistration>();

    [Fact]
    public void EveryProviderSpecificOptionsTypeIsRegistered()
    {
        var types = Registrations.Select(r => r.OptionsType.Name).ToList();

        foreach (var expected in new[]
        {
            "BaGetOptions", "DatabaseOptions", "MirrorOptions", "AccountsOptions", "EmailOptions",
            "AzureBlobStorageOptions", "AzureSearchOptions", "AzureTableOptions", "S3StorageOptions",
            "AliyunStorageOptions", "GoogleCloudStorageOptions", "IISServerOptions",
        })
        {
            Assert.Contains(expected, types);
        }
    }

    [Fact]
    public void TheLoggingLevelAndEventLogKeysAreEditableSettings()
    {
        var level = Catalog.Find("Serilog:MinimumLevel:Default");
        Assert.NotNull(level);
        Assert.Equal(SettingKind.Enum, level!.Kind);
        Assert.False(level.RequiresRestart);
        Assert.Contains("Warning", level.EnumValues);

        Assert.True(Catalog.Find("Serilog:EventLog:Enabled")!.RequiresRestart);
        Assert.True(Catalog.Find("Serilog:EventLog:Source")!.RequiresRestart);
    }

    [Fact]
    public async System.Threading.Tasks.Task ASavedLoggingLevelIsReadBackAndNullRemovesIt()
    {
        var service = _app.Services.GetRequiredService<SettingsService>();
        var level = service.Catalog.Find("Serilog:MinimumLevel:Default")!;

        var saved = await service.SaveAsync(new Dictionary<SettingDescriptor, object> { [level] = "Warning" }, "test", TestContext.Current.CancellationToken);
        Assert.True(saved.Succeeded, string.Join("; ", saved.Errors));
        Assert.Equal("Warning", service.GetState().Single(s => s.Descriptor.Key == level.Key).EffectiveValue);

        var removed = await service.SaveAsync(new Dictionary<SettingDescriptor, object> { [level] = null! }, "test", TestContext.Current.CancellationToken);
        Assert.True(removed.Succeeded, string.Join("; ", removed.Errors));
        Assert.False(service.GetState().Single(s => s.Descriptor.Key == level.Key).IsOverridden);
    }

    [Fact]
    public void EveryPublicSettableScalarPropertyHasADescriptor()
    {
        var keys = new HashSet<string>(Catalog.Descriptors.Select(d => d.Key), StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var registration in Registrations)
        {
            foreach (var key in ExpectedKeys(registration.OptionsType, registration.Section, root: registration.Section == null, depth: 0))
            {
                if (!keys.Contains(key)) missing.Add(key);
            }
        }

        Assert.Empty(missing);
    }

    private static bool IsComplex(Type type)
        => type.IsClass && type != typeof(string) && type != typeof(Uri) && !typeof(IEnumerable).IsAssignableFrom(type);

    private static IEnumerable<string> ExpectedKeys(Type type, string? prefix, bool root, int depth)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.SetMethod?.IsPublic != true) continue;
            if (property.GetCustomAttribute<HiddenSettingAttribute>() != null) continue;

            var key = string.IsNullOrEmpty(prefix) ? property.Name : prefix + ":" + property.Name;
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (!IsComplex(propertyType))
            {
                yield return key;
            }
            else if (!root && depth < 4)
            {
                foreach (var nested in ExpectedKeys(propertyType, key, false, depth + 1)) yield return nested;
            }
        }
    }

    [Fact]
    public void EveryCredentialLikePropertyIsSensitive()
    {
        var offenders = new List<string>();

        foreach (var registration in Registrations)
        {
            foreach (var property in AllProperties(registration.OptionsType, 0))
            {
                if (property.PropertyType != typeof(string) || !CredentialName.IsMatch(property.Name)) continue;
                if (property.GetCustomAttribute<SensitiveSettingAttribute>() == null)
                {
                    #pragma warning disable CS8602
                    offenders.Add(property.DeclaringType.Name + "." + property.Name);
                    #pragma warning restore CS8602
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static IEnumerable<PropertyInfo> AllProperties(Type type, int depth)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod?.IsPublic != true) continue;

            if (IsComplex(property.PropertyType))
            {
                if (depth < 4)
                {
                    foreach (var nested in AllProperties(property.PropertyType, depth + 1)) yield return nested;
                }

                continue;
            }

            yield return property;
        }
    }

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("Database:ConnectionString")]
    [InlineData("Email:Smtp:Password")]
    [InlineData("Accounts:OpenIdConnect:ClientSecret")]
    [InlineData("Validation:RepositorySigningCertificatePassword")]
    [InlineData("Storage:AccessKey")]
    [InlineData("Storage:SecretKey")]
    [InlineData("Storage:AccessKeySecret")]
    [InlineData("Search:ApiKey")]
    public void CredentialsAreSensitiveInTheCatalog(string key)
    {
        var descriptor = Catalog.Find(key);

        Assert.True(descriptor != null, key + " has no descriptor");
        Assert.True(descriptor.IsSensitive, key + " is not sensitive");
    }

    [Fact]
    public void DescriptionsComeFromTheXmlDocumentation()
    {
        var apiKey = Catalog.Find("ApiKey");

        #pragma warning disable CS8602
        Assert.False(string.IsNullOrWhiteSpace(apiKey.Description));
        #pragma warning restore CS8602
        Assert.DoesNotContain("<", apiKey.Description);
        Assert.DoesNotContain("  ", apiKey.Description);
        Assert.Contains("authenticat", apiKey.Description);

        // A see cref is shown as its last segment.
        #pragma warning disable CS8602
        Assert.Contains("ApiKey", Catalog.Find("Accounts:Enabled").Description);
        #pragma warning restore CS8602
    }

    [Theory]
    [InlineData("AllowPackageOverwrites", SettingKind.Boolean, false)]
    [InlineData("IsReadOnlyMode", SettingKind.Boolean, false)]
    [InlineData("PackageDeletionBehavior", SettingKind.Enum, false)]
    [InlineData("RunMigrationsAtStartup", SettingKind.Boolean, true)]
    [InlineData("PathBase", SettingKind.String, true)]
    [InlineData("AllowedHosts", SettingKind.String, true)]
    [InlineData("Mirror:PackageDownloadTimeoutSeconds", SettingKind.Integer, true)]
    [InlineData("Mirror:PackageSource", SettingKind.String, true)]
    [InlineData("Email:Smtp:Port", SettingKind.Integer, true)]
    [InlineData("Accounts:OpenIdConnect:ClientId", SettingKind.String, true)]
    [InlineData("Feeds:EnableV2", SettingKind.Boolean, false)]
    [InlineData("Validation:Enabled", SettingKind.Boolean, false)]
    [InlineData("Site:UseGravatar", SettingKind.Boolean, false)]
    [InlineData("Site:PublicUrl", SettingKind.String, true)]
    [InlineData("Storage:DownloadUriExpiry", SettingKind.TimeSpan, true)]
    public void KindAndRestartClassification(string key, SettingKind kind, bool restart)
    {
        var descriptor = Catalog.Find(key);

        Assert.True(descriptor != null, key + " has no descriptor");
        Assert.Equal(kind, descriptor.Kind);
        Assert.Equal(restart, descriptor.RequiresRestart);
    }

    [Fact]
    public void KeysSharedByTwoTypesAreOneDescriptorWithTheFirstGroup()
    {
        Assert.Single(Catalog.Descriptors, d => string.Equals(d.Key, "Storage:Type", StringComparison.OrdinalIgnoreCase));
        Assert.Single(Catalog.Descriptors, d => string.Equals(d.Key, "Database:ConnectionString", StringComparison.OrdinalIgnoreCase));
        #pragma warning disable CS8602
        Assert.Equal("AzureBlobStorage", Catalog.Find("Storage:DownloadUriExpiry").Group);
        #pragma warning restore CS8602
    }

    [Fact]
    public void TheRootContributesItsScalarsOnly()
    {
        Assert.Contains(Catalog.InSection(string.Empty), d => d.Key == "ApiKey");
        Assert.DoesNotContain(Catalog.Descriptors, d => d.Key == "Database" || d.Key == "Accounts");
        Assert.All(Catalog.InSection(string.Empty), d => Assert.DoesNotContain(":", d.Key));
    }

    [Fact]
    public void InternalStateIsNotListed()
    {
        Assert.Null(Catalog.Find("Email:Suppressed"));
    }

    [Fact]
    public void DocsAnchorFallsBackToThePageTop()
    {
        Assert.EndsWith("/configuration/", SettingsCatalog.DocsAnchor("NoSuchSection"));
        Assert.Contains("#", SettingsCatalog.DocsAnchor("Mirror"));
    }

    [Fact]
    public void TheOverridesFileIsLoadedAfterAppsettingsAndBeforeEnvironmentAndCommandLine()
    {
        var root = (IConfigurationRoot)_app.Services.GetRequiredService<IConfiguration>();
        var providers = root.Providers.ToList();
        #pragma warning disable CS8604
        foreach (var provider in providers) _output.WriteLine(provider.ToString());
        #pragma warning restore CS8604

        var storePath = _app.Services.GetRequiredService<SettingsOverridesStore>().FilePath;
        var overrides = providers.FindIndex(p => IsFile(p, out var path) && SamePath(path, storePath));
        #pragma warning disable CS8602
        var lastAppsettings = providers.FindLastIndex(
            p => IsFile(p, out var path) && Path.GetFileName(path).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
                 && !SamePath(path, storePath));
        #pragma warning restore CS8602
        var environment = providers.FindLastIndex(p => p.GetType().Name.StartsWith("EnvironmentVariables"));
        var commandLine = providers.FindLastIndex(p => p.GetType().Name.StartsWith("CommandLine"));

        Assert.True(overrides >= 0, "no overrides provider");
        Assert.True(overrides > lastAppsettings, "overrides must follow appsettings*.json");
        if (environment >= 0) Assert.True(overrides < environment);
        if (commandLine >= 0) Assert.True(overrides < commandLine);
    }

    private static bool IsFile(IConfigurationProvider provider, out string? path)
    {
        path = null;
        if (provider is not FileConfigurationProvider file) return false;

        #pragma warning disable CS8604
        path = file.Source.FileProvider?.GetFileInfo(file.Source.Path)?.PhysicalPath;
        #pragma warning restore CS8604
        return path != null;
    }

    #pragma warning disable CS8604
    private static bool SamePath(string? a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    #pragma warning restore CS8604
}
