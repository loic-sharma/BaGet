using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BaGet.Core.Tests.Settings;

public class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BaGetSettingsTests", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public SettingsServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "appsettings.Overrides.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    /// Reads the overrides file through the store, so the test needs no JSON configuration package.
    /// </summary>
    private sealed class OverridesSource : FileConfigurationSource
    {
        public override IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new OverridesProvider(this);
        }
    }

    private sealed class OverridesProvider : FileConfigurationProvider
    {
        public OverridesProvider(FileConfigurationSource source) : base(source)
        {
        }

        public override void Load(Stream stream)
        {
            #pragma warning disable CS8602, CS8604
            var store = new SettingsOverridesStore(System.IO.Path.Combine(((PhysicalFileProvider?)Source.FileProvider).Root, Source.Path));
            #pragma warning restore CS8602, CS8604
            #pragma warning disable CS8620
            Data = new Dictionary<string, string?>(store.Read(), StringComparer.OrdinalIgnoreCase);
            #pragma warning restore CS8620
        }
    }

    private SettingsService Create(Dictionary<string, string>? baseValues, Dictionary<string, string>? afterOverrides = null)
    {
        #pragma warning disable CS8620
        var builder = new ConfigurationBuilder().AddInMemoryCollection(baseValues);
        #pragma warning restore CS8620
        builder.Add(new OverridesSource { Path = System.IO.Path.GetFileName(_path), FileProvider = new PhysicalFileProvider(_directory), Optional = true });
        #pragma warning disable CS8620
        if (afterOverrides != null) builder.AddInMemoryCollection(afterOverrides);
        #pragma warning restore CS8620
        var configuration = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddBaGetOptions<BaGetOptions>();
        services.AddBaGetOptions<DatabaseOptions>("Database");
        services.AddBaGetOptions<MirrorOptions>("Mirror");
        services.AddSingleton(new SettingsOverridesStore(_path));
        var provider = services.BuildServiceProvider();

        var registrations = provider.GetServices<SettingsSchemaRegistration>().ToList();
        return new SettingsService(
            configuration,
            new SettingsCatalog(registrations),
            registrations,
            provider,
            NullLogger<SettingsService>.Instance,
            provider.GetRequiredService<SettingsOverridesStore>());
    }

    private static Dictionary<string, string>? Valid() => new()
    {
        ["Database:ConnectionString"] = "Data Source=x",
    };

    #pragma warning disable CS8601, CS8604
    private static IReadOnlyDictionary<SettingDescriptor, object> Change(SettingsService service, string key, object? value)
        => new Dictionary<SettingDescriptor, object> { [service.Catalog.Find(key)] = value };
    #pragma warning restore CS8601, CS8604

    private static SettingState State(SettingsService service, string key)
        => service.GetState().Single(s => s.Descriptor.Key == key);

    [Fact]
    public void AKeyNoSourceSetsShowsTheOptionsDefault()
    {
        var service = Create(Valid());

        Assert.Equal("true", State(service, "RunMigrationsAtStartup").EffectiveValue);
        Assert.False(State(service, "RunMigrationsAtStartup").HasValue);
        Assert.Equal("600", State(service, "Mirror:PackageDownloadTimeoutSeconds").EffectiveValue);
    }

    [Fact]
    public async Task ADefaultTrueBooleanCanBeTurnedOff()
    {
        var service = Create(Valid());
        var state = State(service, "RunMigrationsAtStartup");
        var form = new[] { new KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>(SettingsForm.ValuePrefix + state.Descriptor.Key, new[] { "false" }) };

        var (changes, errors) = SettingsForm.Parse(form, new[] { state });
        Assert.Empty(errors);
        Assert.Single(changes);
        var result = await service.SaveAsync(changes, "admin", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("false", State(service, "RunMigrationsAtStartup").EffectiveValue, ignoreCase: true);
    }

    [Fact]
    public async Task AnUnwritableOverridesLocationFailsTheSaveWithAMessage()
    {
        Directory.CreateDirectory(_path); // a directory where the file should be
        var service = Create(Valid());

        var result = await service.SaveAsync(Change(service, "AllowPackageOverwrites", true), "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains(_path));
    }

    [Fact]
    public async Task AMalformedOverridesFileIsReportedNotThrown()
    {
        var service = Create(Valid());
        File.WriteAllText(_path, "{ not json");

        var states = service.GetState();
        var result = await service.SaveAsync(Change(service, "AllowPackageOverwrites", true), "admin", CancellationToken.None);

        Assert.NotEmpty(states);
        Assert.Contains("not valid JSON", service.OverridesError);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("not valid JSON"));
    }

    [Fact]
    public async Task ASaveFailsForAnIllegalLegalPagePath()
    {
        var service = CreateSite();

        var result = await service.SaveAsync(Change(service, "Site:TermsOfUsePath", "appsettings.Overrides.json"), "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(File.Exists(_path));
    }

    private SettingsService CreateSite()
    {
        #pragma warning disable CS8620
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Valid()).Build();
        #pragma warning restore CS8620
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddBaGetOptions<DatabaseOptions>("Database");
        services.AddBaGetOptions<SiteOptions>("Site");
        services.AddSingleton(new SettingsOverridesStore(_path));
        var provider = services.BuildServiceProvider();
        var registrations = provider.GetServices<SettingsSchemaRegistration>().ToList();
        return new SettingsService(configuration, new SettingsCatalog(registrations), registrations, provider, NullLogger<SettingsService>.Instance, provider.GetRequiredService<SettingsOverridesStore>());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("legal/terms.md", true)]
    [InlineData(@"C:\legal\TERMS.MD", true)]
    [InlineData("appsettings.json", false)]
    [InlineData("appsettings.Overrides.json", false)]
    [InlineData("../terms.md", false)]
    [InlineData(@"a\..\terms.md", false)]
    public void OnlyMarkdownPathsWithoutParentSegmentsAreAllowed(string? path, bool allowed)
        => Assert.Equal(allowed, MarkdownFilePathAttribute.IsAllowed(path!));

    [Fact]
    public async Task ASavedValueTakesEffectAtOnceAndIsMarkedOverridden()
    {
        var service = Create(Valid());

        var result = await service.SaveAsync(Change(service, "AllowPackageOverwrites", true), "admin", CancellationToken.None);

        Assert.True(result.Succeeded);
        var state = State(service, "AllowPackageOverwrites");
        Assert.Equal("true", state.EffectiveValue, ignoreCase: true);
        Assert.True(state.IsOverridden);
        Assert.Null(state.ShadowedBy);
        Assert.False(state.PendingRestart);
    }

    [Fact]
    public async Task ResetRemovesTheOverride()
    {
        var service = Create(Valid());
        await service.SaveAsync(Change(service, "AllowPackageOverwrites", true), "admin", CancellationToken.None);

        await service.SaveAsync(Change(service, "AllowPackageOverwrites", null), "admin", CancellationToken.None);

        var state = State(service, "AllowPackageOverwrites");
        Assert.False(state.IsOverridden);
        Assert.False(state.HasValue);
    }

    [Fact]
    public async Task ASourceAfterTheOverridesFileShadowsTheSavedValue()
    {
        var service = Create(Valid(), new Dictionary<string, string> { ["Mirror:PackageDownloadTimeoutSeconds"] = "5" });

        await service.SaveAsync(Change(service, "Mirror:PackageDownloadTimeoutSeconds", 30L), "admin", CancellationToken.None);

        var shadowed = State(service, "Mirror:PackageDownloadTimeoutSeconds");
        Assert.True(shadowed.IsOverridden);
        Assert.Equal("host setting", shadowed.ShadowedBy);
        Assert.Equal("5", shadowed.EffectiveValue);
        Assert.Null(State(service, "Mirror:Legacy").ShadowedBy);
    }

    [Fact]
    public async Task ASourceBeforeTheOverridesFileDoesNotShadowIt()
    {
        #pragma warning disable CS8604
        var service = Create(new Dictionary<string, string>(Valid()) { ["Mirror:PackageDownloadTimeoutSeconds"] = "5" });
        #pragma warning restore CS8604

        await service.SaveAsync(Change(service, "Mirror:PackageDownloadTimeoutSeconds", 30L), "admin", CancellationToken.None);

        var state = State(service, "Mirror:PackageDownloadTimeoutSeconds");
        Assert.Null(state.ShadowedBy);
        Assert.Equal("30", state.EffectiveValue);
    }

    [Fact]
    public async Task ValidationReportsAFailureTheChangeIntroduces()
    {
        var service = Create(Valid());

        var result = await service.SaveAsync(Change(service, "Mirror:Enabled", true), "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("PackageSource"));
        Assert.False(File.Exists(_path));
        Assert.False(State(service, "Mirror:Enabled").IsOverridden);
    }

    [Fact]
    public async Task ValidationIgnoresFailuresThatAlreadyExist()
    {
        // No database connection string: DatabaseOptions already fails, whatever is saved.
        var service = Create(new Dictionary<string, string>());

        var result = await service.SaveAsync(Change(service, "AllowPackageOverwrites", true), "admin", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public async Task APendingRestartIsReportedForARestartOnlySettingThatChanged()
    {
        var service = Create(Valid());
        Assert.False(State(service, "Mirror:PackageDownloadTimeoutSeconds").PendingRestart);

        await service.SaveAsync(Change(service, "Mirror:PackageDownloadTimeoutSeconds", 30L), "admin", CancellationToken.None);

        Assert.True(State(service, "Mirror:PackageDownloadTimeoutSeconds").PendingRestart);
        Assert.False(State(service, "AllowPackageOverwrites").PendingRestart);
    }

    [Fact]
    public async Task ChangingBackClearsThePendingRestart()
    {
        var service = Create(Valid());
        await service.SaveAsync(Change(service, "Mirror:PackageDownloadTimeoutSeconds", 30L), "admin", CancellationToken.None);

        await service.SaveAsync(Change(service, "Mirror:PackageDownloadTimeoutSeconds", null), "admin", CancellationToken.None);

        Assert.False(State(service, "Mirror:PackageDownloadTimeoutSeconds").PendingRestart);
    }

    [Fact]
    public async Task ASensitiveValueIsNeverReportedBack()
    {
        var service = Create(Valid());

        await service.SaveAsync(Change(service, "Database:ConnectionString", "Data Source=secret"), "admin", CancellationToken.None);

        var state = State(service, "Database:ConnectionString");
        Assert.Null(state.EffectiveValue);
        Assert.True(state.HasValue);
        Assert.True(state.Descriptor.IsSensitive);
    }

    [Fact]
    public async Task AListIsSavedAsAnArrayAndReadBack()
    {
        var service = Create(Valid());
        var descriptor = new SettingDescriptor("Test:Items", "Test", "Test", SettingKind.StringList, typeof(string[]), Array.Empty<string>(), false, false, "");

        var result = await service.SaveAsync(
            new Dictionary<SettingDescriptor, object> { [descriptor] = new[] { "a", "b" } },
            "admin",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("a", new SettingsOverridesStore(_path).Read()["Test:Items:0"]);
    }

    private static Dictionary<string, string> WithValue(string key, string value)
    {
        var values = Valid()!;
        values[key] = value;
        return values;
    }

    private const string OverrideKey = "Serilog:MinimumLevel:Override:";

    [Fact]
    public async Task ALogLevelOverrideIsSavedAndListedWithItsOrigin()
    {
        var service = Create(WithValue(OverrideKey + "Microsoft", "Warning"));

        var result = await service.SetLogLevelOverrideAsync("BaGet.Core.Indexing", "debug", "admin", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Debug", new SettingsOverridesStore(_path).Read()[OverrideKey + "BaGet.Core.Indexing"]);
        var listed = service.GetLogLevelOverrides().ToDictionary(o => o.Source);
        Assert.Equal("Debug", listed["BaGet.Core.Indexing"].Level);
        Assert.True(listed["BaGet.Core.Indexing"].InOverridesFile);
        Assert.Equal("Warning", listed["Microsoft"].Level);
        Assert.False(listed["Microsoft"].InOverridesFile);
    }

    [Fact]
    public async Task ALevelCanBeChangedForASourceTheConfigurationFileSets()
    {
        var service = Create(WithValue(OverrideKey + "Microsoft", "Warning"));

        Assert.True((await service.SetLogLevelOverrideAsync("Microsoft", "Error", "admin", CancellationToken.None)).Succeeded);

        var changed = Assert.Single(service.GetLogLevelOverrides(), o => o.Source == "Microsoft");
        Assert.Equal("Error", changed.Level);
        Assert.True(changed.InOverridesFile);
    }

    [Fact]
    public async Task OnlyAnOverrideSavedHereCanBeRemoved()
    {
        var service = Create(WithValue(OverrideKey + "Microsoft", "Warning"));
        await service.SetLogLevelOverrideAsync("System", "Error", "admin", CancellationToken.None);

        Assert.True((await service.SetLogLevelOverrideAsync("System", null, "admin", CancellationToken.None)).Succeeded);
        Assert.DoesNotContain(service.GetLogLevelOverrides(), o => o.Source == "System");

        var refused = await service.SetLogLevelOverrideAsync("Microsoft", null, "admin", CancellationToken.None);
        Assert.False(refused.Succeeded);
        Assert.Contains("change its level", refused.Errors.Single());
    }

    [Theory]
    [InlineData("", "Debug")]
    [InlineData("A:B", "Debug")]
    [InlineData("Has space", "Debug")]
    [InlineData("Microsoft", "Loud")]
    [InlineData("Microsoft", "")]
    public async Task ABadSourceOrLevelIsRefusedAndNothingIsWritten(string source, string level)
    {
        var service = Create(Valid());

        var result = await service.SetLogLevelOverrideAsync(source, level, "admin", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void SavingWithoutAStoreIsNotAvailable()
    {
        var configuration = new ConfigurationBuilder().Build();
        var service = new SettingsService(
            configuration,
            new SettingsCatalog(Array.Empty<SettingsSchemaRegistration>()),
            Array.Empty<SettingsSchemaRegistration>(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<SettingsService>.Instance);

        Assert.False(service.IsAvailable);
        Assert.Null(service.OverridesPath);
    }
}
