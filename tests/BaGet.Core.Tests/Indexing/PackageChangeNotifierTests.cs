using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Core.Tests.Indexing;

public class PackageChangeNotifierTests
{
    private static readonly NuGetVersion Version = NuGetVersion.Parse("1.0.0");

    [Fact]
    public async Task CallsEveryHandler()
    {
        var first = new Mock<IPackageChangeHandler>();
        var second = new Mock<IPackageChangeHandler>();
        var target = new PackageChangeNotifier(new[] { first.Object, second.Object }, NullLogger<PackageChangeNotifier>.Instance);

        await target.PackageChangedAsync("Id", Version, CancellationToken.None);
        await target.PackageDeletedAsync("Id", Version, CancellationToken.None);

        first.Verify(h => h.OnPackageChangedAsync("Id", Version, It.IsAny<CancellationToken>()), Times.Once);
        second.Verify(h => h.OnPackageChangedAsync("Id", Version, It.IsAny<CancellationToken>()), Times.Once);
        first.Verify(h => h.OnPackageDeletedAsync("Id", Version, It.IsAny<CancellationToken>()), Times.Once);
        second.Verify(h => h.OnPackageDeletedAsync("Id", Version, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AFailingHandlerDoesNotFailTheChangeOrStopTheOthers()
    {
        // The change is already committed; a handler's failure must not undo or fail it.
        var failing = new Mock<IPackageChangeHandler>();
        failing
            .Setup(h => h.OnPackageChangedAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var next = new Mock<IPackageChangeHandler>();
        var target = new PackageChangeNotifier(new[] { failing.Object, next.Object }, NullLogger<PackageChangeNotifier>.Instance);

        await target.PackageChangedAsync("Id", Version, CancellationToken.None);

        next.Verify(h => h.OnPackageChangedAsync("Id", Version, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheCallersCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var handler = new Mock<IPackageChangeHandler>();
        handler
            .Setup(h => h.OnPackageChangedAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var target = new PackageChangeNotifier(new[] { handler.Object }, NullLogger<PackageChangeNotifier>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => target.PackageChangedAsync("Id", Version, cancellation.Token));
    }
}