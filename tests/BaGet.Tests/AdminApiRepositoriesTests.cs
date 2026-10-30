using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Core;
using Xunit;
using static BaGet.Tests.AdminTestSupport;

namespace BaGet.Tests;

/// <summary>
/// <c>/api/v1/repositories</c>: the Repositories, RepositoryEdit and RepositoryDelete pages' operations over HTTP, for every format and
/// kind, with the pages' rules (a name is validated by <see cref="RepositoryNames"/>, the default repository is never deleted, a
/// repository with packages or one that a virtual repository lists is kept, a remote repository's changes ask for the password).
/// </summary>
public class AdminApiRepositoriesTests : IDisposable
{
    private readonly AdminApiHarness _api;

    public AdminApiRepositoriesTests(ITestOutputHelper output)
    {
        _api = new AdminApiHarness(output);
    }

    public void Dispose() => _api.Dispose();

    private static object Remote(string url = "https://example.invalid/v3/index.json") => new { url };

    [Fact]
    public async Task ALocalRepositoryOfEveryFormatIsCreatedListedShownAndDeleted()
    {
        foreach (var format in new[] { "NuGet", "Npm", "Maven", "Docker" })
        {
            var name = format.ToLowerInvariant() + "-repo";
            var created = await _api.PostAsync("/api/v1/repositories", new { name, format, kind = "Local" });
            Assert.True(created.Status == HttpStatusCode.Created, $"{format}: {created.Body}");
            Assert.Equal(format, created.Json["format"]!.GetValue<string>());
            Assert.Contains("/" + (format == "Docker" ? "v2/" + name : "r/" + name), created.Json["feedUrl"]!.GetValue<string>());

            var shown = await _api.GetAsync("/api/v1/repositories/" + name);
            Assert.Equal("Local", shown.Json["repository"]!["kind"]!.GetValue<string>());
        }

        var listed = await _api.GetAsync("/api/v1/repositories?format=Maven");
        Assert.Equal("maven-repo", Assert.Single(listed.Json["items"]!.AsArray())!["name"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync("/api/v1/repositories/maven-repo")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync("/api/v1/repositories/maven-repo")).Status);
    }

    [Fact]
    public async Task InvalidCreateRequestsAreRefusedWithProblems()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsync("/api/v1/repositories", new { name = "Bad Name" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsync("/api/v1/repositories", new { name = "x", format = "Cobol" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsync("/api/v1/repositories", new { name = "x", kind = "Sideways" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsync("/api/v1/repositories", new { name = "x", kind = "Remote" })).Status);

        await _api.PostAsync("/api/v1/repositories", new { name = "taken" });
        var duplicate = await _api.PostAsync("/api/v1/repositories", new { name = "taken" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.Status);
        Assert.Equal("conflict", duplicate.Code);
    }

    [Fact]
    public async Task AnonymousReadIsSwitchedWithPatchAndARepositoryCanBeCreatedPrivate()
    {
        await _api.PostAsync("/api/v1/repositories", new { name = "closed", anonymousRead = false });
        Assert.False((await _api.GetAsync("/api/v1/repositories/closed")).Json["repository"]!["anonymousRead"]!.GetValue<bool>());

        var patched = await _api.SendAsync(HttpMethod.Patch, "/api/v1/repositories/closed", new { anonymousRead = true });
        Assert.Equal(HttpStatusCode.OK, patched.Status);
        Assert.True(patched.Json["anonymousRead"]!.GetValue<bool>());

        Assert.Equal(HttpStatusCode.BadRequest, (await _api.SendAsync(HttpMethod.Patch, "/api/v1/repositories/closed", new { })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.SendAsync(HttpMethod.Patch, "/api/v1/repositories/ghost", new { anonymousRead = true })).Status);
    }

    [Fact]
    public async Task TheDefaultRepositoryARepositoryWithPackagesAndAVirtualMemberAreNotDeleted()
    {
        Assert.Equal(HttpStatusCode.Conflict, (await _api.DeleteAsync("/api/v1/repositories/default")).Status);

        await _api.PostAsync("/api/v1/repositories", new { name = "full" });
        using (var package = TestPackages.Build("Acme.Full", "1.0.0"))
        {
            await _api.App.AddPackageAsync(package, AdminApiHarness.Token, "full");
        }

        var notEmpty = await _api.DeleteAsync("/api/v1/repositories/full");
        Assert.Equal(HttpStatusCode.Conflict, notEmpty.Status);

        await _api.PostAsync("/api/v1/repositories", new { name = "member" });
        await _api.PostAsync("/api/v1/repositories", new { name = "all", kind = "Virtual", @virtual = new { members = new[] { "member" }, deployRepository = "member" } });
        var referenced = await _api.DeleteAsync("/api/v1/repositories/member");
        Assert.Equal(HttpStatusCode.Conflict, referenced.Status);
        Assert.Contains("all", referenced.Body);
    }

    [Fact]
    public async Task AVirtualRepositoryHasItsMembersReplacedAndPreviewsWhereAPackageComesFrom()
    {
        await _api.PostAsync("/api/v1/repositories", new { name = "one" });
        await _api.PostAsync("/api/v1/repositories", new { name = "two" });
        using (var package = TestPackages.Build("Acme.Pick", "1.0.0"))
        {
            await _api.App.AddPackageAsync(package, AdminApiHarness.Token, "two");
        }

        var created = await _api.PostAsync("/api/v1/repositories", new { name = "both", kind = "Virtual" });
        Assert.Equal(HttpStatusCode.Created, created.Status);

        var updated = await _api.PutAsync("/api/v1/repositories/both/virtual", new { members = new[] { "one", "two" }, deployRepository = "one" });
        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.Equal(new[] { "one", "two" }, updated.Json["members"]!.AsArray().Select(m => m!["name"]!.GetValue<string>()).ToArray());
        Assert.Equal("one", updated.Json["deployTarget"]!.GetValue<string>());

        var preview = await _api.PostAsync("/api/v1/repositories/both/virtual/preview", new { packageId = "Acme.Pick", version = "1.0.0" });
        Assert.Equal(HttpStatusCode.OK, preview.Status);
        Assert.Equal("two", preview.Json["answers"]!.GetValue<string>());

        var invalid = await _api.PutAsync("/api/v1/repositories/both/virtual", new { members = new[] { "nope" } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);

        var shown = await _api.GetAsync("/api/v1/repositories/two");
        Assert.Equal("both", Assert.Single(shown.Json["referencedBy"]!.AsArray())!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await _api.PutAsync("/api/v1/repositories/one/virtual", new { members = Array.Empty<string>() })).Status);
    }

    [Fact]
    public async Task ARemoteRepositoryIsEditedTestedAndPurgedAndNeverReturnsItsPassword()
    {
        var created = await _api.PostAsync("/api/v1/repositories", new { name = "up", kind = "Remote", remote = new { url = "https://example.invalid/v3/index.json", username = "u", password = "s3cret" } }, password: Password);
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.False(created.Json["anonymousRead"]!.GetValue<bool>());

        var shown = await _api.GetAsync("/api/v1/repositories/up");
        Assert.DoesNotContain("s3cret", shown.Body);
        Assert.True(shown.Json["remote"]!["hasPassword"]!.GetValue<bool>());
        Assert.Equal("u", shown.Json["remote"]!["username"]!.GetValue<string>());

        var update = await _api.PutAsync("/api/v1/repositories/up/remote", new { url = "https://example.invalid/v3/index.json", username = "u", metadataTtlSeconds = 30 });
        Assert.Equal("confirmation-required", update.Code);

        var saved = await _api.PutAsync("/api/v1/repositories/up/remote", new { url = "https://example.invalid/v3/index.json", username = "u", metadataTtlSeconds = 30 }, password: Password);
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Equal(30, saved.Json["metadataTtlSeconds"]!.GetValue<int>());
        Assert.True(saved.Json["hasPassword"]!.GetValue<bool>());

        var invalid = await _api.PutAsync("/api/v1/repositories/up/remote", new { url = "not a url" }, password: Password);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);

        var cache = await _api.GetAsync("/api/v1/repositories/up/remote/cache");
        Assert.Equal(0, cache.Json["packageCount"]!.GetValue<int>());

        var tested = await _api.PostAsync("/api/v1/repositories/up/remote/test", new { url = "not a url" }, password: Password);
        Assert.Equal(HttpStatusCode.OK, tested.Status);
        Assert.False(tested.Json["success"]!.GetValue<bool>());

        Assert.Equal("confirmation-required", (await _api.PostAsync("/api/v1/repositories/up/remote/purge")).Code);
        Assert.Equal(HttpStatusCode.OK, (await _api.PostAsync("/api/v1/repositories/up/remote/purge", password: Password)).Status);
        Assert.Equal(HttpStatusCode.OK, (await _api.PostAsync("/api/v1/repositories/up/remote/purge-package", new { packageId = "Acme.Lib", version = "1.0.0" }, password: Password)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsync("/api/v1/repositories/up/remote/purge-package", new { version = "1.0.0" }, password: Password)).Status);

        // A local repository has no upstream.
        await _api.PostAsync("/api/v1/repositories", new { name = "local" });
        Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync("/api/v1/repositories/local/remote/cache")).Status);
    }

    [Fact]
    public async Task TheFormatsListNamesEveryModuleItsKindsAndFields()
    {
        var formats = await _api.GetAsync("/api/v1/formats");

        var names = formats.Json.AsArray().Select(f => f!["format"]!.GetValue<string>()).ToArray();
        Assert.Contains("NuGet", names);
        Assert.Contains("Npm", names);
        Assert.Contains("Maven", names);
        Assert.Contains("Docker", names);
        var maven = formats.Json.AsArray().Single(f => f!["format"]!.GetValue<string>() == "Maven")!;
        Assert.Contains(maven["createFields"]!.AsArray(), f => f!["name"]!.GetValue<string>() == "policy");
    }

    [Fact]
    public async Task ARepositorysAccessSummaryNamesWhoHoldsWhat()
    {
        var access = await _api.GetAsync("/api/v1/repositories/default/access");

        Assert.Equal(HttpStatusCode.OK, access.Status);
        Assert.NotEmpty(access.Json["holders"]!.AsArray());
        Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync("/api/v1/repositories/ghost/access")).Status);
    }
}
