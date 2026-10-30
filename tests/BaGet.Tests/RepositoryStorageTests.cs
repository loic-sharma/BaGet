using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Catalog.Database;
using BaGet.Catalog.Storage;
using BaGet.Core;
using BaGet.Validation.Services;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// Files per repository, end to end on the real pipeline and a real file store: the default repository keeps the
/// pre-repository layout, every other one nests it under <c>repos/{name}/</c>, and cleanup stays inside the repository.
/// </summary>
public class RepositoryStorageTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "baget-repo-storage-" + Guid.NewGuid().ToString("N"));
    private readonly BaGetApplication _app;

    public RepositoryStorageTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "Storage:Path", _storage },
            { "Accounts:Enabled", "false" },
            { "PackageDeletionBehavior", "HardDelete" },
        });
        _app.CreateClient().Dispose();
    }

    public void Dispose()
    {
        _app.Dispose();
        try { Directory.Delete(_storage, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private async Task<RepositoryInfo> CreateSecondAsync()
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync("second", RepositoryFormat.NuGet, RepositoryKind.Local, Ct);
    }

    private IServiceScope Scope(RepositoryInfo? repository = null)
        => repository == null
            ? _app.Services.CreateScope()
            : _app.Services.GetRequiredService<IServiceScopeFactory>().CreateRepositoryScope(repository);

    private async Task IndexAsync(RepositoryInfo? repository, string id, string version)
    {
        using var scope = Scope(repository);
        using var package = TestPackages.Build(id, version);
        var result = await scope.ServiceProvider.GetRequiredService<IPackageIndexingService>().IndexAsync(package, Ct);
        Assert.Equal(PackageIndexingResult.Success, result);
    }

    private string[] Files(string subfolder = "")
    {
        var root = Path.Combine(_storage, subfolder);
        return Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(_storage, f).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();
    }

    [Fact]
    public async Task TheDefaultRepositoryStoresPackagesAndCatalogLeavesWhereTheyAlwaysWere()
    {
        await IndexAsync(null, "Legacy.Package", "1.0.0");

        var files = Files();
        Assert.Contains("packages/legacy.package/1.0.0/legacy.package.1.0.0.nupkg", files);
        Assert.Contains("packages/legacy.package/1.0.0/legacy.package.nuspec", files);
        Assert.Single(files, f => f.StartsWith("catalog/", StringComparison.Ordinal) && f.EndsWith(".json", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.StartsWith("repos/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASecondRepositoryStoresItsPackagesAndCatalogLeavesUnderRepos()
    {
        var second = await CreateSecondAsync();

        await IndexAsync(second, "Same.Package", "1.0.0");

        var files = Files();
        Assert.Contains("repos/second/packages/same.package/1.0.0/same.package.1.0.0.nupkg", files);
        Assert.Single(files, f => f.StartsWith("repos/second/catalog/", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.StartsWith("packages/", StringComparison.Ordinal) || f.StartsWith("catalog/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheSameIdAndVersionIsStoredOncePerRepositoryAndHardDeleteStaysInsideOne()
    {
        var second = await CreateSecondAsync();
        var version = NuGetVersion.Parse("1.0.0");
        await IndexAsync(null, "Same.Package", "1.0.0");
        await IndexAsync(second, "Same.Package", "1.0.0");

        using (var scope = Scope(second))
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IPackageDeletionService>()
                .TryDeletePackageAsync("Same.Package", version, Ct));
        }

        Assert.DoesNotContain(Files("repos/second/packages"), f => f.EndsWith(".nupkg", StringComparison.Ordinal));
        Assert.Contains("packages/same.package/1.0.0/same.package.1.0.0.nupkg", Files());

        using (var scope = Scope())
        {
            using var stream = await scope.ServiceProvider.GetRequiredService<IPackageStorageService>()
                .GetPackageStreamAsync("Same.Package", version, Ct);
            Assert.NotNull(stream);
        }
    }

    [Fact]
    public async Task CatalogLeavesAreFoundOnlyThroughTheirOwnRepository()
    {
        var second = await CreateSecondAsync();
        await IndexAsync(second, "Only.Second", "1.0.0");

        string commitId;
        using (var scope = Scope(second))
        {
            var entry = Assert.Single(await scope.ServiceProvider.GetRequiredService<ICatalogDatabase>().GetEntriesAsync(0, 10, Ct));
            commitId = entry.CommitId!;
        }

        // The leaf is where the second repository's reader looks; the default repository's reader finds no entry.
        Assert.Contains($"repos/second/catalog/{commitId}.json", Files());

        using (var scope = Scope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<CatalogReader>().GetLeafOrNullAsync(commitId, Ct));
        }
    }

    [Fact]
    public void StagedSubmissionAndLeafPathsKeepTheirLegacyShapeForTheDefaultRepository()
    {
        var second = new RepositoryInfo(2, "second", RepositoryFormat.NuGet, RepositoryKind.Local);

        Assert.Equal("validation/abc.nupkg", ValidatingPackageSubmissionService.StagedPath(RepositoryInfo.Default, "abc"));
        Assert.Equal("repos/second/validation/abc.nupkg", ValidatingPackageSubmissionService.StagedPath(second, "abc"));
        Assert.Equal("catalog/abc.json", CatalogWriter.LeafPath(RepositoryInfo.Default, "abc"));
        Assert.Equal("repos/second/catalog/abc.json", CatalogWriter.LeafPath(second, "abc"));
        Assert.Equal("staging/abc.nupkg", StoragePackageStagingService.StagedPath("abc"));
    }
}
