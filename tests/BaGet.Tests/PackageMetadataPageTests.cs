using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using Xunit;
using static BaGet.Tests.AdminTestSupport;
using BaGet.Identity.Application.Services.Ownership;

namespace BaGet.Tests;

/// <summary>
/// Plan U7 and B6: the Manage page's vulnerability editor (same rules as the metadata API), the administrator-only
/// verified flag, and the per-version symbol list and delete, which works from the package-to-PDB mapping recorded
/// when a symbol package is indexed.
/// </summary>
public class PackageMetadataPageTests : IDisposable
{
    private const string Second = "second";
    private const string PackageId = "TestData";
    private const string Version = "1.2.3";
    private const string PdbPath = "api/download/symbols/testdata.pdb/16F71ED8DD574AA2AD4A22D29E9C981Bffffffff/testdata.pdb";

    private readonly BaGetApplication _app;
    private readonly Browser _admin;
    private readonly Browser _alice;

    public PackageMetadataPageTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "Accounts:Enabled", "true" }, { "ApiKey", "" } });
        _admin = new Browser(_app);
        _alice = new Browser(_app);
        CreateUserAsync(_app, "root", administrator: true).GetAwaiter().GetResult();
        CreateUserAsync(_app, "alice").GetAwaiter().GetResult();
        SignInAsync(_admin, "root").GetAwaiter().GetResult();
        SignInAsync(_alice, "alice").GetAwaiter().GetResult();
        _app.CreateRepositoryAsync(Second).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _alice.Dispose();
        _app.Dispose();
    }

    private static string ManagePath(string? repository)
        => (repository == null ? "" : $"r/{repository}/") + $"packages/{PackageId}/manage";

    private async Task SeedAsync(string? repository, bool withSymbols, string owner = "alice")
    {
        var token = TestContext.Current.CancellationToken;
        using (var package = TestResources.GetResourceStream(TestResources.Package)!)
        {
            await _app.AddPackageAsync(package, token, repository);
        }

        if (withSymbols)
        {
            using var symbols = TestResources.GetResourceStream(TestResources.SymbolPackage)!;
            await _app.AddSymbolPackageAsync(symbols, token, repository);
        }

        using var scope = await _app.CreateRepositoryScopeAsync(repository, token);
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        context.PackageOwners!.Add(new PackageOwner { PackageId = PackageId, OwnerName = owner });
        await context.SaveChangesAsync(token);
    }

    // The token comes from a page every signed-in user can open: the Manage page itself refuses a stranger.
    private Task<HttpResponseMessage> PostAsync(Browser browser, string? repository, string handler, Dictionary<string, string> fields)
        => browser.PostAsync(ManagePath(repository), fields, action: $"{ManagePath(repository)}?handler={handler}", tokenPage: "account/settings");

    private async Task<HttpStatusCode> DownloadPdbAsync(string? repository)
    {
        using var client = _app.CreateClient();
        using var response = await client.GetAsync((repository == null ? "" : $"r/{repository}/") + PdbPath);

        return response.StatusCode;
    }

    // ---- Symbols (B6) ----

    [Fact]
    public async Task IndexingASymbolPackageRecordsItsPdbsForTheVersion()
    {
        await SeedAsync(null, withSymbols: true);

        using var scope = _app.Services.CreateScope();
        var listed = await scope.ServiceProvider.GetRequiredService<ISymbolRegistry>()
            .ListAsync(PackageId, NuGetVersion.Parse(Version), TestContext.Current.CancellationToken);

        var pdb = Assert.Single(listed);
        Assert.Equal("testdata.pdb", pdb.FileName);
        Assert.Equal("16f71ed8dd574aa2ad4a22d29e9c981bffffffff", pdb.Key);
    }

    [Fact]
    public async Task ManageListsTheSymbolsOfAVersion()
    {
        await SeedAsync(null, withSymbols: true);

        var html = await _alice.GetPageAsync(ManagePath(null));

        Assert.Contains("id=\"manage-symbols\"", html);
        Assert.Contains("testdata.pdb", html);
        Assert.Contains("Delete symbols", html);
    }

    [Fact]
    public async Task ManageSaysWhenNoSymbolIsRecorded()
    {
        await SeedAsync(null, withSymbols: false);

        var html = await _alice.GetPageAsync(ManagePath(null));

        Assert.Contains("manage-symbols-empty", html);
        Assert.DoesNotContain("id=\"manage-symbols\"", html);
    }

    [Fact]
    public async Task DeletingTheSymbolsOfAVersionStopsServingThem()
    {
        await SeedAsync(null, withSymbols: true);
        Assert.Equal(HttpStatusCode.OK, await DownloadPdbAsync(null));

        using var response = await PostAsync(_alice, null, "DeleteSymbols", new() { { "version", Version } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Deleted 1 symbol file(s)", html);
        Assert.Contains("manage-symbols-empty", html);
        Assert.Equal(HttpStatusCode.NotFound, await DownloadPdbAsync(null));
    }

    [Fact]
    public async Task DeletingSymbolsOfAVersionWithNoneSaysSo()
    {
        await SeedAsync(null, withSymbols: false);

        using var response = await PostAsync(_alice, null, "DeleteSymbols", new() { { "version", Version } });

        Assert.Contains("no recorded symbols", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SymbolsAreRecordedAndDeletedPerRepository()
    {
        await SeedAsync(null, withSymbols: true);
        await SeedAsync(Second, withSymbols: true);

        using (var response = await PostAsync(_alice, Second, "DeleteSymbols", new() { { "version", Version } }))
        {
            Assert.Contains("Deleted 1 symbol file(s)", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(HttpStatusCode.NotFound, await DownloadPdbAsync(Second));
        // The default repository holds the same PDB under its own path and its own row.
        Assert.Equal(HttpStatusCode.OK, await DownloadPdbAsync(null));
        Assert.Contains("id=\"manage-symbols\"", await _alice.GetPageAsync(ManagePath(null)));
    }

    [Fact]
    public async Task APdbSharedByTwoVersionsStaysUntilTheLastOneIsDeleted()
    {
        var token = TestContext.Current.CancellationToken;
        const string key = "16F71ED8DD574AA2AD4A22D29E9C981Bffffffff";

        using var scope = _app.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<ISymbolStorageService>();
        var registry = scope.ServiceProvider.GetRequiredService<ISymbolRegistry>();
        await storage.SavePortablePdbContentAsync("shared.pdb", key, new MemoryStream(new byte[] { 1, 2, 3 }), token);
        var symbol = new[] { new SymbolReference("shared.pdb", key) };
        await registry.RecordAsync("Some.Lib", NuGetVersion.Parse("1.0.0"), symbol, token);
        await registry.RecordAsync("Some.Lib", NuGetVersion.Parse("1.0.1"), symbol, token);

        Assert.Equal(1, await registry.DeleteAsync("Some.Lib", NuGetVersion.Parse("1.0.0"), token));
        using (var kept = await storage.GetPortablePdbContentStreamOrNullAsync("shared.pdb", key))
        {
            Assert.NotNull(kept);
        }

        Assert.Equal(1, await registry.DeleteAsync("Some.Lib", NuGetVersion.Parse("1.0.1"), token));
        Assert.Null(await storage.GetPortablePdbContentStreamOrNullAsync("shared.pdb", key));
    }

    [Fact]
    public async Task RecordingTheSameSymbolsTwiceKeepsOneRow()
    {
        var token = TestContext.Current.CancellationToken;
        await SeedAsync(null, withSymbols: true);

        using var symbols = TestResources.GetResourceStream(TestResources.SymbolPackage)!;
        await _app.AddSymbolPackageAsync(symbols, token);

        using var scope = _app.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<ISymbolRegistry>()
            .ListAsync(PackageId, NuGetVersion.Parse(Version), token));
    }

    [Fact]
    public async Task SymbolsIndexedBeforeTheyWereRecordedAreServedButNotListed()
    {
        var token = TestContext.Current.CancellationToken;
        await SeedAsync(null, withSymbols: false);

        // Stored the way an earlier release did: content only, no mapping row.
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISymbolStorageService>().SavePortablePdbContentAsync(
                "testdata.pdb", "16F71ED8DD574AA2AD4A22D29E9C981Bffffffff", new MemoryStream(new byte[] { 1 }), token);
        }

        Assert.Equal(HttpStatusCode.OK, await DownloadPdbAsync(null));
        Assert.Contains("manage-symbols-empty", await _alice.GetPageAsync(ManagePath(null)));
    }

    [Fact]
    public async Task DeletingARepositoryRemovesItsRecordedSymbols()
    {
        var token = TestContext.Current.CancellationToken;
        using (var package = TestResources.GetResourceStream(TestResources.Package)!)
        {
            await _app.AddPackageAsync(package, token, Second);
        }

        using (var symbols = TestResources.GetResourceStream(TestResources.SymbolPackage)!)
        {
            await _app.AddSymbolPackageAsync(symbols, token, Second);
        }

        using var scope = _app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRepositoryAdministration>();
        // A repository with packages cannot be deleted; remove them first the way an administrator does.
        using (var inner = await _app.CreateRepositoryScopeAsync(Second, token))
        {
            await inner.ServiceProvider.GetRequiredService<IPackageDeletionService>()
                .TryHardDeletePackageAsync(PackageId, NuGetVersion.Parse(Version), token);
        }

        await admin.DeleteAsync(Second, token);

        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        Assert.False(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .AnyAsync(Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .IgnoreQueryFilters(context.PackageSymbols!), token));
    }

    [Fact]
    public async Task IndexingSymbolsWhileTheGateIsClosedStoresNothing()
    {
        var token = TestContext.Current.CancellationToken;
        await SeedAsync(null, withSymbols: false);
        _app.Services.GetRequiredService<MaintenanceGate>().Close("test");

        using var symbols = TestResources.GetResourceStream(TestResources.SymbolPackage)!;
        using var scope = _app.Services.CreateScope();
        await Assert.ThrowsAsync<MaintenanceException>(() => scope.ServiceProvider
            .GetRequiredService<ISymbolIndexingService>().IndexAsync(symbols, token));

        _app.Services.GetRequiredService<MaintenanceGate>().Open();
        Assert.Equal(HttpStatusCode.NotFound, await DownloadPdbAsync(null));
    }

    // ---- Vulnerabilities (U7) ----

    private async Task<IReadOnlyList<PackageVulnerability>> VulnerabilitiesAsync(string? repository)
    {
        using var scope = await _app.CreateRepositoryScopeAsync(repository, TestContext.Current.CancellationToken);
        var package = await scope.ServiceProvider.GetRequiredService<IPackageDatabase>()
            .FindOrNullAsync(PackageId, NuGetVersion.Parse(Version), includeUnlisted: true, TestContext.Current.CancellationToken);

        return package!.Vulnerabilities ?? new List<PackageVulnerability>();
    }

    [Fact]
    public async Task AnOwnerAddsAVulnerabilityFromTheManagePage()
    {
        await SeedAsync(null, withSymbols: false);

        using var response = await PostAsync(_alice, null, "AddVulnerability", new()
        {
            { "version", Version },
            { "advisoryUrl", "https://github.com/advisories/GHSA-1234" },
            { "severity", "2" },
            { "versionRange", "[1.0.0,2.0.0)" },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("updated (vulnerability added)", html);
        Assert.Contains("GHSA-1234", html);
        var stored = Assert.Single(await VulnerabilitiesAsync(null));
        Assert.Equal(2, stored.Severity);
        Assert.Equal("[1.0.0,2.0.0)", stored.VersionRange);
    }

    [Fact]
    public async Task AVulnerabilityTheApiWouldRefuseIsRefusedByThePage()
    {
        await SeedAsync(null, withSymbols: false);

        foreach (var fields in new[]
        {
            new Dictionary<string, string> { { "advisoryUrl", "javascript:alert(1)" }, { "severity", "1" }, { "versionRange", "" } },
            new Dictionary<string, string> { { "advisoryUrl", "https://example.com/a" }, { "severity", "9" }, { "versionRange", "" } },
            new Dictionary<string, string> { { "advisoryUrl", "https://example.com/a" }, { "severity", "1" }, { "versionRange", "not a range" } },
        })
        {
            fields["version"] = Version;
            using var response = await PostAsync(_alice, null, "AddVulnerability", fields);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Matches("is not an absolute|outside the range|is not a version range",
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        Assert.Empty(await VulnerabilitiesAsync(null));
    }

    [Fact]
    public async Task RemovingOneVulnerabilityKeepsTheOthers()
    {
        await SeedAsync(null, withSymbols: false);
        foreach (var url in new[] { "https://example.com/one", "https://example.com/two" })
        {
            (await PostAsync(_alice, null, "AddVulnerability", new()
            {
                { "version", Version }, { "advisoryUrl", url }, { "severity", "0" }, { "versionRange", "" },
            })).Dispose();
        }

        var first = (await VulnerabilitiesAsync(null)).Single(v => v.AdvisoryUrl!.EndsWith("one"));

        using var response = await PostAsync(_alice, null, "RemoveVulnerability", new()
        {
            { "version", Version }, { "key", first.Key.ToString() },
        });

        Assert.Contains("updated (vulnerability removed)", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("https://example.com/two", Assert.Single(await VulnerabilitiesAsync(null)).AdvisoryUrl);
    }

    [Fact]
    public async Task VulnerabilitiesAreEditedInTheRepositoryOfThePage()
    {
        await SeedAsync(null, withSymbols: false);
        await SeedAsync(Second, withSymbols: false);

        (await PostAsync(_alice, Second, "AddVulnerability", new()
        {
            { "version", Version }, { "advisoryUrl", "https://example.com/second" }, { "severity", "3" }, { "versionRange", "" },
        })).Dispose();

        Assert.Single(await VulnerabilitiesAsync(Second));
        Assert.Empty(await VulnerabilitiesAsync(null));
    }

    [Fact]
    public async Task AStrangerCannotEditVulnerabilitiesOrDeleteSymbols()
    {
        await SeedAsync(null, withSymbols: true, owner: "root");

        using var stranger = new Browser(_app);
        await CreateUserAsync(_app, "mallory");
        await SignInAsync(stranger, "mallory");

        using (var add = await PostAsync(stranger, null, "AddVulnerability", new()
        {
            { "version", Version }, { "advisoryUrl", "https://example.com/x" }, { "severity", "1" }, { "versionRange", "" },
        }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        }

        using (var delete = await PostAsync(stranger, null, "DeleteSymbols", new() { { "version", Version } }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        }

        Assert.Empty(await VulnerabilitiesAsync(null));
        Assert.Equal(HttpStatusCode.OK, await DownloadPdbAsync(null));
    }

    // ---- Verified (U7) ----

    [Fact]
    public async Task AnAdministratorSetsAndClearsTheVerifiedFlag()
    {
        await SeedAsync(null, withSymbols: false);
        Assert.Contains("not verified", await _admin.GetPageAsync(ManagePath(null)));

        using (var set = await PostAsync(_admin, null, "SetVerified", new() { { "verified", "true" } }))
        {
            var html = await set.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains($"{PackageId} is verified.", html);
            Assert.Contains("Clear verified flag", html);
        }

        using (var scope = _app.Services.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<PackageOwnershipService>()
                .IsVerifiedAsync(PackageId, TestContext.Current.CancellationToken));
        }

        using var clear = await PostAsync(_admin, null, "SetVerified", new() { { "verified", "false" } });
        Assert.Contains("is no longer verified", await clear.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnOwnerCannotVerifyTheirOwnPackage()
    {
        await SeedAsync(null, withSymbols: false);

        var page = await _alice.GetPageAsync(ManagePath(null));
        Assert.DoesNotContain("Mark as verified", page);

        using var response = await PostAsync(_alice, null, "SetVerified", new() { { "verified", "true" } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = _app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<PackageOwnershipService>()
            .IsVerifiedAsync(PackageId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VerifyingAnOwnerlessIdIsNotFound()
    {
        // Versions without owners: the page opens for an administrator, but the flag lives on owner rows.
        using (var package = TestResources.GetResourceStream(TestResources.Package)!)
        {
            await _app.AddPackageAsync(package, TestContext.Current.CancellationToken);
        }

        using var response = await PostAsync(_admin, null, "SetVerified", new() { { "verified", "true" } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Deprecation: every field the metadata API takes ----

    private async Task<PackageDeprecation?> DeprecationAsync()
    {
        using var scope = _app.Services.CreateScope();
        var package = await scope.ServiceProvider.GetRequiredService<IPackageDatabase>()
            .FindOrNullAsync(PackageId, NuGetVersion.Parse(Version), includeUnlisted: true, TestContext.Current.CancellationToken);

        return package?.Deprecation;
    }

    [Fact]
    public async Task DeprecatingFromThePageTakesTheAlternateVersionRange()
    {
        await SeedAsync(null, withSymbols: false);

        using var response = await PostAsync(_alice, null, "Deprecate", new()
        {
            { "version", Version },
            { "reasons", "Legacy" },
            { "alternatePackageId", "Better.Package" },
            { "alternateVersionRange", "[2.0.0, )" },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deprecation = await DeprecationAsync();
        Assert.Equal("Better.Package", deprecation?.AlternatePackageId);
        Assert.Equal("[2.0.0, )", deprecation?.AlternateVersionRange);
    }

    [Theory]
    [InlineData("", "[2.0.0, )", "needs an alternate package")]
    [InlineData("Better.Package", "not a range", "is not a version range")]
    public async Task TheAlternateVersionRangeFollowsTheApiRules(string alternate, string range, string expected)
    {
        await SeedAsync(null, withSymbols: false);

        using var response = await PostAsync(_alice, null, "Deprecate", new()
        {
            { "version", Version },
            { "reasons", "Legacy" },
            { "alternatePackageId", alternate },
            { "alternateVersionRange", range },
        });

        Assert.Contains(expected, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Null(await DeprecationAsync());
    }
}
