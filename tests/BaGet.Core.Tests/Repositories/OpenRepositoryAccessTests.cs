using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace BaGet.Core.Tests.Repositories;

/// <summary>
/// What the UI offers (every repository, of every format), and the single-repository mode (Azure Table, AzureSearch): the
/// default repository only, with the switchers and pickers asking <see cref="IRepositoryAccess.SupportsMultipleRepositories"/>.
/// </summary>
public class OpenRepositoryAccessTests
{
    private static readonly RepositoryInfo Zeta = new(3, "zeta", RepositoryFormat.NuGet, RepositoryKind.Local);
    private static readonly RepositoryInfo Alpha = new(2, "alpha", RepositoryFormat.NuGet, RepositoryKind.Local);

    private static OpenRepositoryAccess Access(bool multiple, params RepositoryInfo[] repositories)
    {
        var service = new Mock<IRepositoryService>();
        service.SetupGet(s => s.SupportsMultipleRepositories).Returns(multiple);
        service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RepositoryInfo>)repositories);

        return new OpenRepositoryAccess(service.Object);
    }

    [Fact]
    public async Task ListsEveryRepositoryDefaultFirstThenByName()
    {
        var access = Access(true, Zeta, RepositoryInfo.Default, Alpha);

        var readable = await access.ReadableAsync(null, CancellationToken.None);
        var writable = await access.WritableAsync(null, CancellationToken.None);

        Assert.Equal(new[] { "default", "alpha", "zeta" }, readable.Select(r => r.Name));
        Assert.Equal(readable, writable);
    }

    [Fact]
    public async Task OffersRepositoriesOfEveryFormat()
    {
        // The pages of a repository decide per format (RepositoryPageGate); the listing is the same for every format.
        var npm = new RepositoryInfo(9, "packages-js", RepositoryFormat.Npm, RepositoryKind.Local);
        var access = Access(true, RepositoryInfo.Default, npm);

        Assert.Equal(new[] { "default", "packages-js" }, (await access.ReadableAsync(null, CancellationToken.None)).Select(r => r.Name));
        Assert.Equal(new[] { "default", "packages-js" }, (await access.WritableAsync(null, CancellationToken.None)).Select(r => r.Name));
        Assert.True(await access.CanReadAsync(null, npm, CancellationToken.None));
        Assert.True(await access.CanReadAsync(null, Alpha, CancellationToken.None));
    }

    [Fact]
    public async Task ASingleRepositoryBackendOffersTheDefaultOnly()
    {
        var access = Access(false, RepositoryInfo.Default, Alpha);

        Assert.False(access.SupportsMultipleRepositories);
        Assert.Equal(new[] { "default" }, (await access.ReadableAsync(null, CancellationToken.None)).Select(r => r.Name));
        Assert.Equal(new[] { "default" }, (await access.WritableAsync(null, CancellationToken.None)).Select(r => r.Name));
    }

    [Fact]
    public void ThePathPrefixIsEmptyForTheDefaultAndNamedForOthers()
    {
        Assert.Equal(string.Empty, RepositoryInfo.Default.PathPrefix);
        Assert.Equal("r/alpha/", Alpha.PathPrefix);
    }
}
