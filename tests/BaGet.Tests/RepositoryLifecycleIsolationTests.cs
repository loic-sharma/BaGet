using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BaGet.Identity.Application.Services.Accounts;

namespace BaGet.Tests;

/// <summary>
/// Over HTTP, the same ID and version live in the default repository (<c>/</c>) and a second one
/// (<c>/r/second/</c>), and every write in one - push, unlist, relist, deprecate, delete - leaves the other's search,
/// autocomplete, version list, registration, V2 feed and catalog byte-for-byte as they were. Runs in both directions.
/// </summary>
public abstract class RepositoryLifecycleIsolationTestsBase : IDisposable
{
    private const string ApiKey = "lifecycle-key";
    private const string Second = "second";

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;
    private readonly string? _acting;
    private readonly string? _other;

    protected RepositoryLifecycleIsolationTestsBase(ITestOutputHelper output, string? acting, string? deletionBehavior = null)
    {
        var settings = new Dictionary<string, string> { { "ApiKey", ApiKey } };
        if (deletionBehavior != null) settings["PackageDeletionBehavior"] = deletionBehavior;

        _app = new BaGetApplication(output, configOverrides: settings);
        _client = _app.CreateClient();
        _app.CreateRepositoryAsync(Second).GetAwaiter().GetResult();

        _acting = acting;
        _other = acting == null ? Second : null;
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private static string Url(string? repository, string path) => repository == null ? path : $"r/{repository}/{path}";

    private async Task<HttpStatusCode> SendAsync(HttpMethod method, string? repository, string path, string? json = null)
    {
        using var request = new HttpRequestMessage(method, Url(repository, path));
        request.Headers.Add("X-NuGet-ApiKey", ApiKey);
        if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private async Task PushAsync(string? repository, string id, string version)
    {
        using var package = TestPackages.Build(id, version);
        using var request = TestPackages.PushRequest(package, ApiKey);
        request.RequestUri = new Uri(Url(repository, "api/v2/package"), UriKind.Relative);

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<string> GetAsync(string? repository, string path)
    {
        using var response = await _client.GetAsync(Url(repository, path), TestContext.Current.CancellationToken);
        return $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}";
    }

    private async Task<int> HitsAsync(string? repository, string query)
    {
        var token = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync(Url(repository, "v3/search?q=" + query), token);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token)).RootElement.GetProperty("totalHits").GetInt32();
    }

    /// <summary>Every read surface of one repository, concatenated, for before/after comparison.</summary>
    private async Task<string> ObserveAsync(string? repository)
    {
        var observed = new StringBuilder();
        foreach (var path in new[]
        {
            "v3/search?q=", "v3/autocomplete?q=Life", "v3/autocomplete?id=Life.Package",
            "v3/package/life.package/index.json", "v3/registration/life.package/index.json",
            "api/v2/FindPackagesById()?id='Life.Package'", "api/v2/Packages()", "v3/catalog/index.json",
        })
        {
            observed.AppendLine(path).AppendLine(await GetAsync(repository, path));
        }

        // The catalog page, where the entries are.
        var index = JsonDocument.Parse((await GetAsync(repository, "v3/catalog/index.json"))[4..]);
        foreach (var page in index.RootElement.GetProperty("items").EnumerateArray())
        {
            // The advertised URL already carries the repository prefix.
            var path = new Uri(page.GetProperty("@id").GetString()!).AbsolutePath.TrimStart('/');
            observed.AppendLine(await GetAsync(null, path));
        }

        return observed.ToString();
    }

    private async Task AssertOtherUntouchedAsync(Func<Task> action)
    {
        var before = await ObserveAsync(_other);
        await action();
        Assert.Equal(before, await ObserveAsync(_other));
    }

    [Fact]
    public async Task EveryWriteInOneRepositoryLeavesTheOtherUntouched()
    {
        await PushAsync(null, "Life.Package", "1.0.0");
        await PushAsync(Second, "Life.Package", "1.0.0");
        const string version = "Life.Package/1.0.0";

        // Push another version to one only.
        await AssertOtherUntouchedAsync(() => PushAsync(_acting, "Life.Package", "2.0.0"));
        Assert.Contains("2.0.0", await GetAsync(_acting, "v3/package/life.package/index.json"));
        Assert.DoesNotContain("2.0.0", await GetAsync(_other, "v3/package/life.package/index.json"));

        // Deprecate, then remove the deprecation.
        await AssertOtherUntouchedAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Put, _acting, $"v3/package/{version}/deprecation", @"{""reasons"":[""Legacy""],""message"":""old""}"));
            Assert.Contains("deprecation", await GetAsync(_acting, "v3/registration/life.package/index.json"));
        });
        Assert.DoesNotContain("deprecation", await GetAsync(_other, "v3/registration/life.package/index.json"));
        await AssertOtherUntouchedAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Delete, _acting, $"v3/package/{version}/deprecation"));
            Assert.DoesNotContain("deprecation", await GetAsync(_acting, "v3/registration/life.package/index.json"));
        });

        // Unlist (the default delete behaviour) and relist.
        Assert.Equal(1, await HitsAsync(_other, "Life"));
        await AssertOtherUntouchedAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Delete, _acting, $"api/v2/package/{version}"));
            Assert.Equal(1, await HitsAsync(_acting, "Life")); // 2.0.0 is still listed here
            Assert.Equal(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Delete, _acting, "api/v2/package/Life.Package/2.0.0"));
            Assert.Equal(0, await HitsAsync(_acting, "Life"));
        });
        Assert.Equal(1, await HitsAsync(_other, "Life"));

        await AssertOtherUntouchedAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.OK, await SendAsync(HttpMethod.Post, _acting, $"api/v2/package/{version}"));
            Assert.Equal(1, await HitsAsync(_acting, "Life"));
        });
    }

    [Fact]
    public async Task APackageOnlyInOneRepositoryIsNotInTheOthersSearchAutocompleteOrFeed()
    {
        await PushAsync(_acting, "Only.Here", "1.0.0");

        foreach (var (path, needle) in new[]
        {
            ("v3/search?q=Only", "Only.Here"), ("v3/autocomplete?q=Only", "Only.Here"),
            ("api/v2/Packages()", "Only.Here"), ("v3/catalog/index.json", "\"count\":1"),
        })
        {
            Assert.Contains(needle, await GetAsync(_acting, path));
            Assert.DoesNotContain(needle, await GetAsync(_other, path));
        }

        // Writes against the other repository for that ID find nothing.
        Assert.Equal(HttpStatusCode.NotFound, await SendAsync(HttpMethod.Delete, _other, "api/v2/package/Only.Here/1.0.0"));
        Assert.Equal(HttpStatusCode.NotFound, await SendAsync(HttpMethod.Post, _other, "api/v2/package/Only.Here/1.0.0"));
        Assert.Equal(HttpStatusCode.NotFound, await SendAsync(HttpMethod.Put, _other, "v3/package/Only.Here/1.0.0/deprecation", @"{""reasons"":[""Legacy""]}"));
        Assert.Contains("Only.Here", await GetAsync(_acting, "v3/search?q=Only"));
    }

    [Fact]
    public async Task APushOfAnExistingIdAndVersionConflictsOnlyWithinItsRepository()
    {
        await PushAsync(_acting, "Dup.Package", "1.0.0");

        using var package = TestPackages.Build("Dup.Package", "1.0.0");
        using var request = TestPackages.PushRequest(package, ApiKey);
        request.RequestUri = new Uri(Url(_acting, "api/v2/package"), UriKind.Relative);
        using var again = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        await PushAsync(_other, "Dup.Package", "1.0.0");
    }

    [Fact]
    public async Task SymbolsPushedToOneRepositoryAreServedOnlyThere()
    {
        var token = TestContext.Current.CancellationToken;
        const string download = "api/download/symbols/testdata.pdb/16F71ED8DD574AA2AD4A22D29E9C981Bffffffff/testdata.pdb";

        using (var package = TestResources.GetResourceStream(TestResources.Package)!)
        using (var push = TestPackages.PushRequest(package, ApiKey))
        {
            push.RequestUri = new Uri(Url(_acting, "api/v2/package"), UriKind.Relative);
            using var pushed = await _client.SendAsync(push, token);
            Assert.Equal(HttpStatusCode.Created, pushed.StatusCode);
        }

        using (var symbols = TestResources.GetResourceStream(TestResources.SymbolPackage)!)
        using (var push = TestPackages.PushRequest(symbols, ApiKey))
        {
            push.RequestUri = new Uri(Url(_acting, "api/v2/symbol"), UriKind.Relative);
            using var pushed = await _client.SendAsync(push, token);
            Assert.Equal(HttpStatusCode.Created, pushed.StatusCode);
        }

        using var here = await _client.GetAsync(Url(_acting, download), token);
        using var there = await _client.GetAsync(Url(_other, download), token);
        Assert.Equal(HttpStatusCode.OK, here.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, there.StatusCode);
    }
}

public class DefaultRepositoryLifecycleIsolationTests(ITestOutputHelper output)
    : RepositoryLifecycleIsolationTestsBase(output, null);

public class SecondRepositoryLifecycleIsolationTests(ITestOutputHelper output)
    : RepositoryLifecycleIsolationTestsBase(output, "second");

/// <summary>A hard delete removes the files and rows of one repository's copy only.</summary>
public class RepositoryHardDeleteIsolationTests : IDisposable
{
    private const string ApiKey = "harddelete-key";

    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public RepositoryHardDeleteIsolationTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", ApiKey },
            { "PackageDeletionBehavior", "HardDelete" },
        });
        _client = _app.CreateClient();
        _app.CreateRepositoryAsync("second").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    [Theory]
    [InlineData(null, "second")]
    [InlineData("second", null)]
    public async Task DeletingOneCopyKeepsTheOthersRowsAndFiles(string? acting, string? other)
    {
        var token = TestContext.Current.CancellationToken;
        string Url(string? repository, string path) => repository == null ? path : $"r/{repository}/{path}";

        foreach (var repository in new[] { acting, other })
        {
            using var package = TestPackages.Build("Gone.Package", "1.0.0");
            using var push = TestPackages.PushRequest(package, ApiKey);
            push.RequestUri = new Uri(Url(repository, "api/v2/package"), UriKind.Relative);
            using var pushed = await _client.SendAsync(push, token);
            Assert.Equal(HttpStatusCode.Created, pushed.StatusCode);
        }

        var nupkg = "v3/package/gone.package/1.0.0/gone.package.1.0.0.nupkg";
        var bytes = await _client.GetByteArrayAsync(Url(other, nupkg), token);

        using var delete = new HttpRequestMessage(HttpMethod.Delete, Url(acting, "api/v2/package/Gone.Package/1.0.0"));
        delete.Headers.Add("X-NuGet-ApiKey", ApiKey);
        using var deleted = await _client.SendAsync(delete, token);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var gone = await _client.GetAsync(Url(acting, nupkg), token);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        using var goneIndex = await _client.GetAsync(Url(acting, "v3/package/gone.package/index.json"), token);
        Assert.Equal(HttpStatusCode.NotFound, goneIndex.StatusCode);

        Assert.Equal(bytes, await _client.GetByteArrayAsync(Url(other, nupkg), token));
        Assert.Contains("1.0.0", await _client.GetStringAsync(Url(other, "v3/package/gone.package/index.json"), token));
    }
}

/// <summary>
/// Ownership is per repository: claiming an ID in one repository neither blocks nor grants anything in the other, and
/// an API key acts in whichever repository it is used against, within its owner's rights there.
/// </summary>
public class RepositoryOwnershipIsolationTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public RepositoryOwnershipIsolationTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output, configOverrides: new Dictionary<string, string>
        {
            { "ApiKey", "global" },
            { "Accounts:Enabled", "true" },
        });
        _client = _app.CreateClient();
        _app.CreateRepositoryAsync("second").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private async Task<string> CreateKeyAsync(string userName)
    {
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<BaGet.Core.BaGetUser>>();
        var user = new BaGet.Core.BaGetUser { UserName = userName };
        Assert.True((await users.CreateAsync(user, "Passw0rd!")).Succeeded);

        return await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
            .CreateAsync(user.Id, "test", new[] { BaGet.Core.ApiKeyScopes.Push }, "*", null, CancellationToken.None);
    }

    private async Task<HttpStatusCode> PushAsync(string? repository, string id, string key)
    {
        using var package = TestPackages.Build(id, "1.0.0");
        using var request = TestPackages.PushRequest(package, key);
        request.RequestUri = new Uri(repository == null ? "api/v2/package" : $"r/{repository}/api/v2/package", UriKind.Relative);

        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    [Fact]
    public async Task ClaimingAnIdInOneRepositoryDoesNotBlockAnotherUserInTheOther()
    {
        var alice = await CreateKeyAsync("alice");
        var bob = await CreateKeyAsync("bob");

        Assert.Equal(HttpStatusCode.Created, await PushAsync(null, "Shared.Name", alice));
        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync(null, "Shared.Name", bob));

        // In the second repository nobody owns the ID yet, so bob claims it there.
        Assert.Equal(HttpStatusCode.Created, await PushAsync("second", "Shared.Name", bob));
        Assert.Equal(HttpStatusCode.Forbidden, await PushAsync("second", "Shared.Name", alice));

        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BaGet.Core.IContext>();
        var owners = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(context.PackageOwners!), TestContext.Current.CancellationToken);
        Assert.Equal(
            new[] { (BaGet.Core.Repository.DefaultKey, "alice"), (2, "bob") },
            owners.Select(o => (o.RepositoryKey, o.OwnerName!)).OrderBy(o => o.Item1));
    }
}
