using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Database.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Core.Tests.Repositories;

/// <summary>A second format for the tests: what a later phase's npm module will be, minus the protocol.</summary>
public sealed class FakeNpmModule : IFormatModule
{
    public RepositoryFormat Format => RepositoryFormat.Npm;
    public string DisplayName => "npm";
    public string RoutePrefix => "npm";
    public string FeedPath => "npm/";
    public IReadOnlyCollection<RepositoryKind> CreatableKinds { get; } = new[] { RepositoryKind.Local };
    public IReadOnlyList<string> StorageFolders { get; } = new[] { "npm" };

    /// <summary>Makes the module claim it stores blobs too large to buffer (a Docker-like format).</summary>
    public bool NeedsStreaming { get; init; }

    bool IFormatModule.RequiresStreamingStorage => NeedsStreaming;

    public IReadOnlyList<FormatCreateField> CreateFields { get; } = new[]
    {
        new FormatCreateField("scope", "Default scope", "Packages without a scope are refused when set.", Required: false),
    };

    public string? CreateSettings(IReadOnlyDictionary<string, string?> fields)
    {
        fields.TryGetValue("scope", out var scope);
        if (!string.IsNullOrEmpty(scope) && !scope.StartsWith('@')) throw new ArgumentException("A scope starts with @.");

        return string.IsNullOrEmpty(scope) ? null : "{\"scope\":\"" + scope + "\"}";
    }

    public IReadOnlyList<FormatLink> RepositoryLinks(RepositoryInfo repository)
        => new[] { FormatLink.ToPath("registry", "Registry", "npm/") };

    public async Task<IReadOnlyDictionary<int, int>> CountArtifactsAsync(IContext context, CancellationToken cancellationToken)
        => await context.NpmPackages!.IgnoreQueryFilters().GroupBy(p => p.RepositoryKey)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

    public async Task<FormatUsage> GetUsageAsync(IContext context, int repositoryKey, CancellationToken cancellationToken)
        => new(await context.NpmPackages!.IgnoreQueryFilters().CountAsync(p => p.RepositoryKey == repositoryKey, cancellationToken), false);

    public async Task StagePurgeAsync(IContext context, int repositoryKey, CancellationToken cancellationToken)
        => context.NpmPackages!.RemoveRange(await context.NpmPackages!.IgnoreQueryFilters().Where(p => p.RepositoryKey == repositoryKey).ToListAsync(cancellationToken));
}

/// <summary>
/// The format layer: the registry's integrity checks, creating repositories through it, NuGet counted and purged by its module
/// and a second format's repositories handled by the same common code.
/// </summary>
public class FormatLayerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "BaGetTests", Guid.NewGuid().ToString("N") + ".db");
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    public FormatLayerTests() => Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    private async Task<SqliteContext> NewContextAsync()
    {
        var context = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite($"Data Source={_path}").Options);
        await context.RunMigrationsAsync(Token);

        return context;
    }

    private static FormatRegistry WithNpm() => new(new IFormatModule[] { new NuGetFormatModule(), new FakeNpmModule() });

    private sealed class ClashingModule : IFormatModule
    {
        private readonly RepositoryFormat _format;
        private readonly string _prefix;
        private readonly string[] _folders;

        public ClashingModule(RepositoryFormat format, string prefix, params string[] folders)
        {
            _format = format;
            _prefix = prefix;
            _folders = folders;
        }

        public RepositoryFormat Format => _format;
        public string DisplayName => "x";
        public string RoutePrefix => _prefix;
        public string FeedPath => "x/";
        public IReadOnlyCollection<RepositoryKind> CreatableKinds => Array.Empty<RepositoryKind>();
        public IReadOnlyList<string> StorageFolders => _folders;
        public IReadOnlyList<FormatCreateField> CreateFields => Array.Empty<FormatCreateField>();
        public string? CreateSettings(IReadOnlyDictionary<string, string?> fields) => null;
        public IReadOnlyList<FormatLink> RepositoryLinks(RepositoryInfo repository) => Array.Empty<FormatLink>();
        public Task<IReadOnlyDictionary<int, int>> CountArtifactsAsync(IContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FormatUsage> GetUsageAsync(IContext context, int repositoryKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task StagePurgeAsync(IContext context, int repositoryKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    // ---- registry ----

    [Fact]
    public void TheDefaultRegistryKnowsNuGetOnly()
    {
        var module = Assert.Single(FormatRegistry.Default.Modules);
        Assert.Equal(RepositoryFormat.NuGet, module.Format);
        Assert.Null(FormatRegistry.Default.Find(RepositoryFormat.Npm));
        Assert.Throws<NotSupportedException>(() => FormatRegistry.Default.Require(RepositoryFormat.Npm));
    }

    [Fact]
    public void NuGetKeepsTheRoutesAndFoldersThatPredateTheLayer()
    {
        var nuget = new NuGetFormatModule();

        Assert.Equal(string.Empty, nuget.RoutePrefix);
        Assert.Equal("v3/index.json", nuget.FeedPath);
        Assert.Equal(new[] { "packages", "symbols", "catalog", "validation", "staging" }, nuget.StorageFolders);
        Assert.Equal(new[] { RepositoryKind.Local, RepositoryKind.Remote }, nuget.CreatableKinds);
    }

    [Fact]
    public void ModulesAreListedByFormatWhateverTheRegistrationOrder()
    {
        var registry = new FormatRegistry(new IFormatModule[] { new FakeNpmModule(), new NuGetFormatModule() });

        Assert.Equal(new[] { RepositoryFormat.NuGet, RepositoryFormat.Npm }, registry.Modules.Select(m => m.Format));
    }

    [Fact]
    public void TwoModulesForOneFormatAreRefused()
    {
        var e = Assert.Throws<InvalidOperationException>(() => new FormatRegistry(new IFormatModule[] { new NuGetFormatModule(), new NuGetFormatModule() }));
        Assert.Contains("two modules", e.Message);
    }

    [Fact]
    public void ASharedStorageFolderIsRefused()
    {
        var e = Assert.Throws<InvalidOperationException>(() => new FormatRegistry(new IFormatModule[]
        {
            new NuGetFormatModule(),
            new ClashingModule(RepositoryFormat.Npm, "npm", "Packages"),
        }));
        Assert.Contains("storage folder", e.Message);
    }

    [Fact]
    public void ClaimingTheFolderOfTheNonDefaultRepositoriesIsRefused()
    {
        var e = Assert.Throws<InvalidOperationException>(() => new FormatRegistry(new IFormatModule[]
        {
            new NuGetFormatModule(),
            new ClashingModule(RepositoryFormat.Npm, "npm", RepositoryStorage.ReposFolder),
        }));
        Assert.Contains("non-default", e.Message);
    }

    [Fact]
    public void ASharedRoutePrefixIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => new FormatRegistry(new IFormatModule[]
        {
            new NuGetFormatModule(),
            new ClashingModule(RepositoryFormat.Npm, string.Empty, "npm"),
        }));
    }

    [Fact]
    public void ACapabilityIsFoundByFormat()
    {
        var services = new ServiceCollection();
        services.AddScoped<IFormatSearchProvider, NuGetFormatSearchProvider>();
        services.AddSingleton<ISearchService, NullSearchService>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetFormatService<IFormatSearchProvider>(RepositoryFormat.NuGet));
        Assert.Null(scope.ServiceProvider.GetFormatService<IFormatSearchProvider>(RepositoryFormat.Npm));
        Assert.Null(scope.ServiceProvider.GetFormatService<IFormatScanner>(RepositoryFormat.NuGet));
    }

    // ---- creating ----

    private sealed class BufferingStorage : IStorageService
    {
        public Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoragePutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task AFormatThatNeedsStreamingIsRefusedOnAStoreThatCannotStreamAndAllowedOnOneThatCan()
    {
        using var context = await NewContextAsync();
        var formats = new FormatRegistry(new IFormatModule[] { new NuGetFormatModule(), new FakeNpmModule { NeedsStreaming = true } });

        var refused = new RepositoryService(context, formats: formats, storage: new BufferingStorage());
        var e = await Assert.ThrowsAsync<NotSupportedException>(() => refused.CreateAsync("big", RepositoryFormat.Npm, RepositoryKind.Local, new Dictionary<string, string?>(), true, Token));
        Assert.Contains("BufferingStorage", e.Message);
        Assert.Null(await refused.FindAsync("big", Token));

        // NuGet does not need it, so the same store still takes a NuGet repository.
        await refused.CreateAsync("feed", RepositoryFormat.NuGet, RepositoryKind.Local, Token);

        var allowed = new RepositoryService(context, formats: formats, storage: new NullStorageService());
        Assert.NotNull(await allowed.CreateAsync("big", RepositoryFormat.Npm, RepositoryKind.Local, new Dictionary<string, string?>(), true, Token));
    }

    [Fact]
    public async Task AFormatWithoutAModuleCannotBeCreated()
    {
        using var context = await NewContextAsync();
        var service = new RepositoryService(context);

        var e = await Assert.ThrowsAsync<NotSupportedException>(() => service.CreateAsync("webapp", RepositoryFormat.Npm, RepositoryKind.Local, Token));
        Assert.Contains("not available", e.Message);
        Assert.Null(await service.FindAsync("webapp", Token));
    }

    [Fact]
    public async Task NuGetRefusalsReadAsBefore()
    {
        using var context = await NewContextAsync();
        var service = new RepositoryService(context);

        var virtualRefusal = await Assert.ThrowsAsync<NotSupportedException>(() => service.CreateAsync("v", RepositoryFormat.NuGet, RepositoryKind.Virtual, Token));
        Assert.Equal("A virtual repository needs its members: create it with IVirtualRepositoryAdministration.", virtualRefusal.Message);
    }

    [Fact]
    public async Task AModuleDecidesWhichKindsCanBeCreatedAndTurnsFieldsIntoSettings()
    {
        using var context = await NewContextAsync();
        var service = new RepositoryService(context, formats: WithNpm());

        var kind = await Assert.ThrowsAsync<NotSupportedException>(() => service.CreateAsync("remote-webapp", RepositoryFormat.Npm, RepositoryKind.Remote, Token));
        Assert.Equal("Only local npm repositories can be created here.", kind.Message);

        var created = await service.CreateAsync("webapp", RepositoryFormat.Npm, RepositoryKind.Local,
            new Dictionary<string, string?> { ["scope"] = "@acme" }, anonymousRead: false, Token);

        Assert.Equal(RepositoryFormat.Npm, created.Format);
        var row = await context.Repositories!.AsNoTracking().SingleAsync(r => r.Name == "webapp", Token);
        Assert.Equal("{\"scope\":\"@acme\"}", row.Settings);
        Assert.False(row.AnonymousRead);

        var invalid = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync("webapp2", RepositoryFormat.Npm, RepositoryKind.Local,
            new Dictionary<string, string?> { ["scope"] = "acme" }, anonymousRead: true, Token));
        Assert.Contains("@", invalid.Message);
        Assert.Null(await service.FindAsync("webapp2", Token));
    }

    [Fact]
    public async Task FieldsAreNotAcceptedForARemoteRepository()
    {
        using var context = await NewContextAsync();
        var service = new RepositoryService(context, formats: WithNpm());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync("r", RepositoryFormat.NuGet, RepositoryKind.Remote,
            new Dictionary<string, string?>(), anonymousRead: true, Token));
    }

    // ---- counting, usage, purge ----

    [Fact]
    public async Task EachModuleCountsItsOwnArtifactsAndTheListSumsThem()
    {
        using var context = await NewContextAsync();
        var formats = WithNpm();
        var service = new RepositoryService(context, formats: formats);
        var npm = await service.CreateAsync("webapp", RepositoryFormat.Npm, RepositoryKind.Local, new Dictionary<string, string?>(), true, Token);
        var feed = await service.CreateAsync("feed", RepositoryFormat.NuGet, RepositoryKind.Local, Token);
        AddNpmPackage(context, npm.Key, "left-pad");
        AddNpmPackage(context, npm.Key, "@acme/tool");
        await context.SaveChangesAsync(Token);

        var summaries = await new RepositoryAdministration(context, formats: formats).ListAsync(Token);

        Assert.Equal(2, summaries.Single(r => r.Name == "webapp").PackageCount);
        Assert.Equal(0, summaries.Single(r => r.Name == "feed").PackageCount);
        Assert.Equal(0, summaries.Single(r => r.IsDefault).PackageCount);
        Assert.Equal(feed.Key, summaries.Single(r => r.Name == "feed").Key);
    }

    [Fact]
    public async Task ARepositoryWithArtifactsOfItsFormatCannotBeDeletedAndAnEmptyOneIsPurged()
    {
        using var context = await NewContextAsync();
        var formats = WithNpm();
        var service = new RepositoryService(context, formats: formats);
        var npm = await service.CreateAsync("webapp", RepositoryFormat.Npm, RepositoryKind.Local, new Dictionary<string, string?>(), true, Token);
        AddNpmPackage(context, npm.Key, "left-pad");
        await context.SaveChangesAsync(Token);
        var admin = new RepositoryAdministration(context, formats: formats);

        await Assert.ThrowsAsync<RepositoryNotEmptyException>(() => admin.DeleteAsync("webapp", Token));

        context.NpmPackages!.RemoveRange(await context.NpmPackages!.IgnoreQueryFilters().ToListAsync(Token));
        await context.SaveChangesAsync(Token);
        await admin.DeleteAsync("webapp", Token);

        Assert.Null(await service.FindAsync("webapp", Token));
    }

    [Fact]
    public async Task DeletingANuGetRepositoryStillRefusesPackagesAndPurgesItsHistory()
    {
        using var context = await NewContextAsync();
        var service = new RepositoryService(context);
        var feed = await service.CreateAsync("feed", RepositoryFormat.NuGet, RepositoryKind.Local, Token);
        context.PackageOwners!.Add(new PackageOwner { RepositoryKey = feed.Key, PackageId = "a", OwnerName = "bob" });
        context.PackageSymbols!.Add(new PackageSymbol { RepositoryKey = feed.Key, PackageId = "a", PackageVersion = "1.0.0", FileName = "a.pdb", SymbolKey = "k", Created = DateTime.UtcNow });
        await context.SaveChangesAsync(Token);

        await new RepositoryAdministration(context).DeleteAsync("feed", Token);

        Assert.False(await context.PackageOwners!.IgnoreQueryFilters().AnyAsync(Token));
        Assert.False(await context.PackageSymbols!.IgnoreQueryFilters().AnyAsync(Token));
        Assert.Null(await service.FindAsync("feed", Token));
    }

    [Fact]
    public async Task ARepositoryWhoseFormatHasNoModuleIsStillListedAndDeletable()
    {
        using var context = await NewContextAsync();
        context.Repositories!.Add(new Repository { Name = "orphan", Format = RepositoryFormat.Npm, Kind = RepositoryKind.Local, Created = DateTime.UtcNow });
        await context.SaveChangesAsync(Token);
        var admin = new RepositoryAdministration(context);

        Assert.Equal(0, (await admin.ListAsync(Token)).Single(r => r.Name == "orphan").PackageCount);
        await admin.DeleteAsync("orphan", Token);
        Assert.Null(await admin.FindAsync("orphan", Token));
    }

    // ---- the NuGet adapters ----

    [Fact]
    public async Task TheNuGetIndexerMapsTheIndexingResults()
    {
        foreach (var (given, expected) in new[]
        {
            (PackageIndexingResult.Success, FormatIndexResult.Indexed),
            (PackageIndexingResult.Accepted, FormatIndexResult.Accepted),
            (PackageIndexingResult.PackageAlreadyExists, FormatIndexResult.AlreadyExists),
            (PackageIndexingResult.InvalidPackage, FormatIndexResult.Invalid),
        })
        {
            var indexer = new NuGetFormatIndexer(new FixedIndexing(given));

            Assert.Equal(expected, await indexer.IndexAsync(new MemoryStream(), Token));
            Assert.Equal(RepositoryFormat.NuGet, indexer.Format);
        }
    }

    [Fact]
    public async Task TheNuGetSearchProviderForwardsTheQueryAndMapsTheHits()
    {
        var search = new RecordingSearch();
        var page = await new NuGetFormatSearchProvider(search).SearchAsync(new FormatSearchQuery("json", 5, 10, true), Token);

        Assert.Equal("json", search.Last!.Query);
        Assert.Equal(5, search.Last.Skip);
        Assert.Equal(10, search.Last.Take);
        Assert.True(search.Last.IncludePrerelease);
        Assert.Equal(7, page.TotalHits);
        var hit = Assert.Single(page.Hits);
        Assert.Equal(new FormatSearchHit("Newtonsoft.Json", "13.0.1", "d", 42), hit);
    }

    // ---- the npm tables ----

    [Fact]
    public async Task NpmPackagesAreIsolatedPerRepositoryAndTheSameNameIsAllowedInTwo()
    {
        using var context = await NewContextAsync();
        var service = new RepositoryService(context, formats: WithNpm());
        var a = await service.CreateAsync("a", RepositoryFormat.Npm, RepositoryKind.Local, new Dictionary<string, string?>(), true, Token);
        var b = await service.CreateAsync("b", RepositoryFormat.Npm, RepositoryKind.Local, new Dictionary<string, string?>(), true, Token);
        AddNpmPackage(context, a.Key, "left-pad");
        AddNpmPackage(context, b.Key, "left-pad");
        await context.SaveChangesAsync(Token);

        var scope = new RepositoryContext();
        scope.Use(a);
        context.RepositoryScope = scope;
        var visible = await context.NpmPackages!.ToListAsync(Token);

        Assert.Equal(a.Key, Assert.Single(visible).RepositoryKey);
        Assert.Equal(2, await context.NpmPackages!.IgnoreQueryFilters().CountAsync(Token));
    }

    [Fact]
    public async Task ANameIsUniquePerRepository()
    {
        using var context = await NewContextAsync();
        AddNpmPackage(context, Repository.DefaultKey, "left-pad");
        await context.SaveChangesAsync(Token);

        AddNpmPackage(context, Repository.DefaultKey, "left-pad");
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task VersionsAndDistTagsAreUniquePerPackageAndFollowThePackageWhenItIsDeleted()
    {
        using var context = await NewContextAsync();
        var package = AddNpmPackage(context, Repository.DefaultKey, "@acme/tool");
        await context.SaveChangesAsync(Token);
        context.NpmVersions!.Add(NewVersion(package.Key, "1.0.0"));
        context.NpmDistTags!.Add(new NpmDistTag { PackageKey = package.Key, Tag = "latest", Version = "1.0.0" });
        await context.SaveChangesAsync(Token);

        context.NpmVersions!.Add(NewVersion(package.Key, "1.0.0"));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
        context.ChangeTracker.Clear();

        context.NpmDistTags!.Add(new NpmDistTag { PackageKey = package.Key, Tag = "latest", Version = "1.0.0" });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
        context.ChangeTracker.Clear();

        context.NpmPackages!.Remove(await context.NpmPackages!.SingleAsync(Token));
        await context.SaveChangesAsync(Token);

        Assert.False(await context.NpmVersions!.AnyAsync(Token));
        Assert.False(await context.NpmDistTags!.AnyAsync(Token));
    }

    [Fact]
    public async Task AManifestLongerThanAnyBoundedColumnIsStored()
    {
        using var context = await NewContextAsync();
        var package = AddNpmPackage(context, Repository.DefaultKey, "big");
        await context.SaveChangesAsync(Token);
        var version = NewVersion(package.Key, "1.0.0");
        version.Manifest = new string('x', 5_000_000);
        context.NpmVersions!.Add(version);
        await context.SaveChangesAsync(Token);
        context.ChangeTracker.Clear();

        Assert.Equal(5_000_000, (await context.NpmVersions!.SingleAsync(Token)).Manifest!.Length);
    }

    private static NpmPackage AddNpmPackage(IContext context, int repositoryKey, string name)
    {
        var package = new NpmPackage { RepositoryKey = repositoryKey, Name = name, Created = DateTime.UtcNow, Modified = DateTime.UtcNow };
        context.NpmPackages!.Add(package);

        return package;
    }

    private static NpmVersion NewVersion(int packageKey, string version) => new()
    {
        PackageKey = packageKey,
        Version = version,
        Integrity = "sha512-abc",
        Shasum = new string('a', 40),
        TarballSize = 10,
        Manifest = "{}",
        Published = DateTime.UtcNow,
    };

    private sealed class FixedIndexing : IPackageIndexingService
    {
        private readonly PackageIndexingResult _result;

        public FixedIndexing(PackageIndexingResult result) => _result = result;

        public Task<PackageIndexingResult> IndexAsync(Stream? stream, CancellationToken cancellationToken) => Task.FromResult(_result);
    }

    private sealed class RecordingSearch : ISearchService
    {
        public SearchRequest? Last { get; private set; }

        public Task<BaGet.Protocol.Models.SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            Last = request;

            return Task.FromResult(new BaGet.Protocol.Models.SearchResponse
            {
                TotalHits = 7,
                Data = new[] { new BaGet.Protocol.Models.SearchResult { PackageId = "Newtonsoft.Json", Version = "13.0.1", Description = "d", TotalDownloads = 42 } },
            });
        }

        public Task<BaGet.Protocol.Models.AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BaGet.Protocol.Models.AutocompleteResponse> ListPackageVersionsAsync(VersionsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DependentsResponse> FindDependentsAsync(string? packageId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
