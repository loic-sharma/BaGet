using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace BaGet.Tests;

/// <summary>Host-level defaults: forwarded headers, the key ring, form limits and the exit code.</summary>
public class HostHardeningTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BaGetHostHardening", Guid.NewGuid().ToString("N"));

    public HostHardeningTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private Microsoft.AspNetCore.Builder.WebApplication Build(params string[] settings)
    {
        var args = new[]
        {
            "--Database:ConnectionString=Data Source=" + Path.Combine(_directory, "BaGet.db"),
            "--Storage:Path=" + Path.Combine(_directory, "Packages"),
            "--SettingsOverridesPath=" + Path.Combine(_directory, "appsettings.Overrides.json"),
            "--Telemetry:Enabled=false",
            "--Serilog:WriteTo:Console:Enabled=false",
            "--Serilog:WriteTo:File:Enabled=false",
            "--Serilog:EventLog:Enabled=false",
        }.Concat(settings.Select(s => "--" + s)).ToArray();

        return Program.BuildApplication(args);
    }

    private static ForwardedHeadersOptions Forwarded(Microsoft.AspNetCore.Builder.WebApplication app)
        => app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

    [Fact]
    public async Task ForwardedHeadersAreTrustedFromLoopbackOnlyByDefault()
    {
        await using var app = Build();

        var options = Forwarded(app);

        Assert.True(options.KnownProxies.Count + options.KnownIPNetworks.Count > 0);
        Assert.All(options.KnownProxies, proxy => Assert.True(IPAddress.IsLoopback(proxy)));
        Assert.All(options.KnownIPNetworks, network => Assert.True(network.Contains(IPAddress.Loopback) || network.Contains(IPAddress.IPv6Loopback)));
    }

    [Fact]
    public async Task TrustAnyProxyClearsTheKnownLists()
    {
        await using var app = Build("Proxy:TrustAnyProxy=true");

        var options = Forwarded(app);

        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public async Task ListedProxiesAndNetworksAreAdded()
    {
        await using var app = Build("Proxy:TrustedProxies=10.0.0.5; fd00::1", "Proxy:TrustedNetworks=172.16.0.0/12");

        var options = Forwarded(app);

        Assert.Contains(IPAddress.Parse("10.0.0.5"), options.KnownProxies);
        Assert.Contains(IPAddress.Parse("fd00::1"), options.KnownProxies);
        Assert.Contains(System.Net.IPNetwork.Parse("172.16.0.0/12"), options.KnownIPNetworks);
    }

    [Theory]
    [InlineData("Proxy:TrustedProxies=not-an-ip")]
    [InlineData("Proxy:TrustedNetworks=10.0.0.0")]
    public async Task AnInvalidProxyListFailsClearly(string setting)
    {
        await using var app = Build(setting);

        Assert.Throws<InvalidOperationException>(() => Forwarded(app));
    }

    [Fact]
    public async Task AFormIsLimitedToOneMebibyteUnlessTheActionRaisesIt()
    {
        await using var app = Build();

        Assert.Equal(1024 * 1024, app.Services.GetRequiredService<IOptions<FormOptions>>().Value.MultipartBodyLengthLimit);
    }

    [Fact]
    public async Task TheKeyRingIsPersistedBesideTheOverridesFileByDefault()
    {
        await using var app = Build();

        app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("secret");

        var folder = Path.Combine(_directory, "DataProtection-Keys");
        Assert.True(Directory.Exists(folder));
        Assert.NotEmpty(Directory.GetFiles(folder, "key-*.xml"));
    }

    [Fact]
    public async Task TheKeyRingFolderCanBeConfigured()
    {
        var custom = Path.Combine(_directory, "keys");
        await using var app = Build("DataProtection:KeyPath=" + custom);

        app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("secret");

        Assert.NotEmpty(Directory.GetFiles(custom, "key-*.xml"));
        Assert.False(Directory.Exists(Path.Combine(_directory, "DataProtection-Keys")));
    }

    [Theory]
    [InlineData(null, "DataProtection-Keys")]
    [InlineData("keys", "keys")]
    public void ARelativeKeyPathStartsAtTheOverridesFileFolder(string? configured, string expectedName)
    {
        var overrides = Path.Combine(_directory, "appsettings.Overrides.json");

        Assert.Equal(Path.Combine(_directory, expectedName), HostDataProtectionExtensions.ResolveKeyFolder(configured, overrides));
    }

    [Fact]
    public void AKeyFolderThatCannotBeWrittenStopsStartupWithAMessageNamingTheSetting()
    {
        var file = Path.Combine(_directory, "not-a-folder");
        File.WriteAllText(file, "x");

        var e = Assert.Throws<InvalidOperationException>(() => HostDataProtectionExtensions.EnsureWritable(Path.Combine(file, "keys")));

        Assert.Contains("DataProtection:KeyPath", e.Message);
        HostDataProtectionExtensions.EnsureWritable(Path.Combine(_directory, "keys"));
    }

    [Fact]
    public async Task InvalidConfigurationExitsWithOne()
    {
        await using var app = Build("Database:Type=NoSuchDatabase");

        Assert.Equal(1, await Program.RunAsync(app, Array.Empty<string>()));
    }

    [Fact]
    public async Task ARefusedCommandExitsWithOne()
    {
        await using var app = Build("Accounts:Enabled=false");

        Assert.Equal(1, await Program.RunAsync(app, new[] { "account", "reset-password", "nobody" }));
    }

    [Fact]
    public async Task TheOverridesFileFollowsTheLastAppSettingsFile()
    {
        // Development adds user secrets, also a JSON source; the overrides file must sit right after the last appsettings*.json,
        // so that user secrets outrank it.
        await using var app = Build();
        var configuration = (Microsoft.Extensions.Configuration.IConfigurationRoot)app.Configuration;

        var jsonPaths = configuration.Providers
            .OfType<Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider>()
            .Select(p => Path.GetFileName(p.Source.Path ?? string.Empty))
            .ToList();

        var overrides = jsonPaths.FindIndex(p => p.Equals("appsettings.Overrides.json", StringComparison.OrdinalIgnoreCase));
        var lastAppSettings = jsonPaths.FindLastIndex(p => p.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) && !p.Equals("appsettings.Overrides.json", StringComparison.OrdinalIgnoreCase));
        Assert.True(overrides > lastAppSettings);
    }
}
