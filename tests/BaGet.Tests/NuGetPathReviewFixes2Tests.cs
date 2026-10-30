using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using BaGet.Catalog.Database;
using BaGet.Catalog.Storage;
using BaGet.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Tests;

/// <summary>Second-round regression tests of the NuGet-path code review: catalog lease, V2 latest flags, pending reject.</summary>
public class NuGetPathReviewFixes2Tests
{
    private readonly ITestOutputHelper _output;

    public NuGetPathReviewFixes2Tests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ACatalogCommitDoesNotWaitForAnUnreachableLeaseStore()
    {
        var lease = new UnreachableLease();
        var catalog = new Mock<ICatalogDatabase>();
        catalog.SetupGet(c => c.IsSupported).Returns(true);
        var storage = new Mock<IStorageService>();
        storage.Setup(x => x.PutAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoragePutResult.Success);
        var writer = new CatalogWriter(
            catalog.Object,
            Mock.Of<IPackageDatabase>(),
            Mock.Of<IPackageStorageService>(),
            storage.Object,
            new SystemTime(),
            NullLogger<CatalogWriter>.Instance,
            lease: lease);

        var started = DateTime.UtcNow;
        await writer.OnPackageDeletedAsync("A", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken);

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.Equal(1, lease.Attempts);
        catalog.Verify(c => c.AddAsync(It.IsAny<CatalogEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheLatestFlagsStayRelativeToVersionsTheClientDoesNotSee()
    {
        var ct = TestContext.Current.CancellationToken;
        using var app = new BaGetApplication(_output);
        await app.AddPackageAsync(TestPackages.Build("Flags.Pkg", "1.0.0"), ct);
        await app.AddPackageAsync(TestPackages.Build("Flags.Pkg", "2.0.0-beta"), ct);
        var client = app.CreateClient();

        using var response = await client.GetAsync("api/v2/Search()?searchTerm='Flags.Pkg'&includePrerelease=false", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dataServices = (XNamespace)"http://schemas.microsoft.com/ado/2007/08/dataservices";
        var entry = Assert.Single(XDocument.Parse(await response.Content.ReadAsStringAsync(ct)).Root!.Elements((XNamespace)"http://www.w3.org/2005/Atom" + "entry"));
        Assert.Equal("1.0.0", entry.Descendants(dataServices + "NormalizedVersion").Single().Value);
        Assert.Equal("true", entry.Descendants(dataServices + "IsLatestVersion").Single().Value);
        Assert.Equal("false", entry.Descendants(dataServices + "IsAbsoluteLatestVersion").Single().Value);
    }

    private sealed class UnreachableLease : IJobLease, IJobLeaseAvailability
    {
        public int Attempts;

        public ValueTask<IAsyncDisposable?> TryAcquireAsync(string job, CancellationToken cancellationToken)
        {
            Attempts++;
            return new ValueTask<IAsyncDisposable?>((IAsyncDisposable?)null);
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
