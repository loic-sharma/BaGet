using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests.Storage;

/// <summary>
/// Where each repository keeps its files: the default repository keeps the layout that predates repositories
/// byte for byte; every other repository nests it under <c>repos/{name}/</c>.
/// </summary>
public class RepositoryStorageLayoutTests
{
    private static readonly RepositoryInfo Second = new(2, "second", RepositoryFormat.NuGet, RepositoryKind.Local);
    private static readonly RepositoryInfo Third = new(3, "third", RepositoryFormat.NuGet, RepositoryKind.Local);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Key = "0123456789abcdef0123456789abcdef";

    private static IRepositoryContext Using(RepositoryInfo repository)
    {
        var context = new RepositoryContext();
        context.Use(repository);
        return context;
    }

    private static Package NewPackage() => new() { Id = "My.Package", Version = NuGetVersion.Parse("1.2.3-Beta") };

    private static Stream Bytes(string text) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));

    private static async Task SaveAsync(PackageStorageService service, string content)
    {
        var result = await service.SavePackageContentAsync(
            NewPackage(), Bytes(content), Bytes("nuspec " + content), Bytes("readme " + content), Bytes("icon " + content), Ct);
        Assert.Equal(PackageSaveResult.Success, result);
    }

    private static async Task<string> ReadAsync(Stream? stream)
    {
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static string[] PackageFiles(string root)
        => new[]
        {
            Path.Combine(root, "icon"),
            Path.Combine(root, "my.package.1.2.3-beta.nupkg"),
            Path.Combine(root, "my.package.nuspec"),
            Path.Combine(root, "readme"),
        };

    [Fact]
    public async Task TheDefaultRepositoryKeepsTheLegacyPackagePathsExactly()
    {
        var storage = new MemoryStorage();
        var service = new PackageStorageService(storage, NullLogger<PackageStorageService>.Instance, Using(RepositoryInfo.Default));

        await SaveAsync(service, "a");

        Assert.Equal(
            PackageFiles(Path.Combine("packages", "my.package", "1.2.3-beta")),
            storage.Paths.OrderBy(p => p, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task WithoutARepositoryContextPackagesAreStoredAsInTheDefaultRepository()
    {
        var storage = new MemoryStorage();
        var service = new PackageStorageService(storage, NullLogger<PackageStorageService>.Instance);

        await SaveAsync(service, "a");

        Assert.Contains(Path.Combine("packages", "my.package", "1.2.3-beta", "my.package.1.2.3-beta.nupkg"), storage.Paths);
    }

    [Fact]
    public async Task AnotherRepositoryNestsItsPackagesUnderRepos()
    {
        var storage = new MemoryStorage();
        var service = new PackageStorageService(storage, NullLogger<PackageStorageService>.Instance, Using(Second));

        await SaveAsync(service, "a");

        Assert.Equal(
            PackageFiles(Path.Combine("repos", "second", "packages", "my.package", "1.2.3-beta")),
            storage.Paths.OrderBy(p => p, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task TheSameIdAndVersionIsStoredSeparatelyInTwoRepositories()
    {
        var storage = new MemoryStorage();
        var logger = NullLogger<PackageStorageService>.Instance;
        var inDefault = new PackageStorageService(storage, logger, Using(RepositoryInfo.Default));
        var inSecond = new PackageStorageService(storage, logger, Using(Second));

        await SaveAsync(inDefault, "default-bytes");
        await SaveAsync(inSecond, "second-bytes");

        var version = NuGetVersion.Parse("1.2.3-beta");
        Assert.Equal("default-bytes", await ReadAsync(await inDefault.GetPackageStreamAsync("My.Package", version, Ct)));
        Assert.Equal("second-bytes", await ReadAsync(await inSecond.GetPackageStreamAsync("My.Package", version, Ct)));
        Assert.Equal(8, storage.Paths.Count);
    }

    [Fact]
    public async Task APackageStoredInOneRepositoryIsNotFoundInAnother()
    {
        var storage = new MemoryStorage();
        var logger = NullLogger<PackageStorageService>.Instance;
        await SaveAsync(new PackageStorageService(storage, logger, Using(Second)), "a");

        var other = new PackageStorageService(storage, logger, Using(Third));
        var found = await other.GetPackageStreamAsync("My.Package", NuGetVersion.Parse("1.2.3-beta"), Ct);

        Assert.Null(found);
    }

    [Fact]
    public async Task DeletingInOneRepositoryLeavesTheOthersFiles()
    {
        var storage = new MemoryStorage();
        var logger = NullLogger<PackageStorageService>.Instance;
        var inDefault = new PackageStorageService(storage, logger, Using(RepositoryInfo.Default));
        var inSecond = new PackageStorageService(storage, logger, Using(Second));
        await SaveAsync(inDefault, "d");
        await SaveAsync(inSecond, "s");

        await inSecond.DeleteAsync("My.Package", NuGetVersion.Parse("1.2.3-beta"), Ct);

        Assert.Equal(4, storage.Paths.Count);
        Assert.All(storage.Paths, p => Assert.StartsWith("packages", p));

        await inDefault.DeleteAsync("My.Package", NuGetVersion.Parse("1.2.3-beta"), Ct);
        Assert.Empty(storage.Paths);
    }

    [Fact]
    public async Task AnInvalidRepositoryNameNeverBecomesAPath()
    {
        var storage = new MemoryStorage();
        var evil = new RepositoryInfo(9, "../escape", RepositoryFormat.NuGet, RepositoryKind.Local);
        var service = new PackageStorageService(storage, NullLogger<PackageStorageService>.Instance, Using(evil));

        await Assert.ThrowsAsync<ArgumentException>(() => SaveAsync(service, "a"));
        Assert.Empty(storage.Paths);
    }

    [Fact]
    public async Task TheDefaultRepositoryKeepsTheLegacySymbolPath()
    {
        var storage = new MemoryStorage();
        var service = new SymbolStorageService(storage, Using(RepositoryInfo.Default));

        await service.SavePortablePdbContentAsync("Foo.pdb", Key + "1", Bytes("pdb"), Ct);

        Assert.Equal(new[] { Path.Combine("symbols", "foo.pdb", Key + "ffffffff") }, storage.Paths.ToArray());
    }

    [Fact]
    public async Task SymbolsPushedToAnotherRepositoryAreNotServedFromTheDefault()
    {
        var storage = new MemoryStorage();
        var inSecond = new SymbolStorageService(storage, Using(Second));
        var inDefault = new SymbolStorageService(storage, Using(RepositoryInfo.Default));

        await inSecond.SavePortablePdbContentAsync("Foo.pdb", Key + "1", Bytes("pdb"), Ct);

        Assert.Equal(new[] { Path.Combine("repos", "second", "symbols", "foo.pdb", Key + "ffffffff") }, storage.Paths.ToArray());
        Assert.Null(await inDefault.GetPortablePdbContentStreamOrNullAsync("Foo.pdb", Key + "1"));
        Assert.Equal("pdb", await ReadAsync(await inSecond.GetPortablePdbContentStreamOrNullAsync("Foo.pdb", Key + "1")));
    }

    [Fact]
    public async Task SymbolFilenamesStillCannotEscapeTheRepositoryFolder()
    {
        var service = new SymbolStorageService(new MemoryStorage(), Using(Second));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SavePortablePdbContentAsync("../foo.pdb", Key, Bytes("pdb"), Ct));
    }

    [Fact]
    public async Task StagedUploadsLiveUnderTheRepositoriesStagingPrefix()
    {
        var storage = new MemoryStorage();
        var inDefault = Staging(storage, RepositoryInfo.Default);
        var inSecond = Staging(storage, Second);

        var legacy = await inDefault.StageAsync(Bytes("x"), "owner", Ct);
        var nested = await inSecond.StageAsync(Bytes("y"), "owner", Ct);

        Assert.Contains($"staging/{legacy}.nupkg", storage.Paths);
        Assert.Contains($"staging/{legacy}.json", storage.Paths);
        Assert.Contains($"repos/second/staging/{nested}.nupkg", storage.Paths);
        Assert.Contains($"repos/second/staging/{nested}.json", storage.Paths);
    }

    [Fact]
    public async Task ATokenStagedForOneRepositoryCannotBeOpenedOrRemovedFromAnother()
    {
        var storage = new MemoryStorage();
        var token = await Staging(storage, Second).StageAsync(Bytes("y"), "owner", Ct);
        var other = Staging(storage, Third);

        Assert.Null(await other.OpenAsync(token, "owner", Ct));
        Assert.False(await other.RemoveAsync(token, "owner", Ct));
        Assert.Equal("y", await ReadAsync(await Staging(storage, Second).OpenAsync(token, "owner", Ct)));
    }

    [Fact]
    public async Task SweepingOneRepositoryLeavesTheOthersUploads()
    {
        var storage = new MemoryStorage();
        await Staging(storage, RepositoryInfo.Default).StageAsync(Bytes("x"), "owner", Ct);
        await Staging(storage, Second).StageAsync(Bytes("y"), "owner", Ct);

        var swept = await Staging(storage, Second).SweepAsync(DateTime.UtcNow.AddDays(1), Ct);

        Assert.Equal(1, swept);
        Assert.Equal(2, storage.Paths.Count);
        Assert.All(storage.Paths, p => Assert.StartsWith("staging/", p));
    }

    private static StoragePackageStagingService Staging(MemoryStorage storage, RepositoryInfo repository)
        => new(storage, new SystemTime(), NullLogger<StoragePackageStagingService>.Instance, Using(repository));

    private sealed class MemoryStorage : IStorageService, IListableStorageService
    {
        private readonly ConcurrentDictionary<string, byte[]> _files = new();

        public ICollection<string> Paths => _files.Keys;

        public Task<Stream> GetAsync(string path, CancellationToken cancellationToken = default)
            => _files.TryGetValue(path, out var bytes)
                ? Task.FromResult<Stream>(new MemoryStream(bytes))
                : throw new FileNotFoundException(path);

        public Task<Uri> GetDownloadUriAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async Task<StoragePutResult> PutAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            return _files.TryAdd(path, copy.ToArray()) ? StoragePutResult.Success : StoragePutResult.Conflict;
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            _files.TryRemove(path, out _);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAsync(string pathPrefix, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(
                _files.Keys.Where(k => k.StartsWith(pathPrefix + "/", StringComparison.Ordinal)).ToList());
    }
}
