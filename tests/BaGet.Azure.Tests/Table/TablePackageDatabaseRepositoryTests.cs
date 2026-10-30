using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using Xunit;

namespace BaGet.Azure.Tests.Table;

/// <summary>
/// The table holds the default repository's packages only; any other repository is refused before the table is
/// touched (so these tests need no storage account).
/// </summary>
public class TablePackageDatabaseRepositoryTests
{
    private static readonly CancellationToken Cancellation = CancellationToken.None;

    private sealed class FixedRepository : IRepositoryContext
    {
        public FixedRepository(RepositoryInfo current) => Current = current;
        public RepositoryInfo Current { get; private set; }
        public void Use(RepositoryInfo repository) => Current = repository;
    }

    private static TablePackageDatabase Create(RepositoryInfo repository)
    {
        var client = new CloudTableClient(
            new Uri("http://127.0.0.1:1/devstoreaccount1"),
            new StorageCredentials("devstoreaccount1", Convert.ToBase64String(new byte[32])));

        return new TablePackageDatabase(
            new TableOperationBuilder(),
            client,
            NullLogger<TablePackageDatabase>.Instance,
            new FixedRepository(repository));
    }

    private static readonly RepositoryInfo Second = new(2, "second", RepositoryFormat.NuGet, RepositoryKind.Local);

    [Fact]
    public async Task RefusesEveryOperationInANonDefaultRepositoryNamingIt()
    {
        var database = Create(Second);
        var version = NuGetVersion.Parse("1.0.0");

        var errors = new[]
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => database.AddAsync(new Package { Id = "P", Version = version }, Cancellation)),
            await Assert.ThrowsAsync<NotSupportedException>(() => database.ExistsAsync("P", Cancellation)),
            await Assert.ThrowsAsync<NotSupportedException>(() => database.ExistsAsync("P", version, Cancellation)),
            await Assert.ThrowsAsync<NotSupportedException>(() => database.FindAsync("P", true, Cancellation)),
            await Assert.ThrowsAsync<NotSupportedException>(() => database.FindOrNullAsync("P", version, true, Cancellation)),
            await Assert.ThrowsAsync<NotSupportedException>(() => database.UnlistPackageAsync("P", version, Cancellation)),
            await Assert.ThrowsAsync<NotSupportedException>(() => database.RelistPackageAsync("P", version, Cancellation)),
            await Assert.ThrowsAsync<NotSupportedException>(() => database.HardDeletePackageAsync("P", version, Cancellation)),
            await Assert.ThrowsAsync<NotSupportedException>(() => database.AddDownloadAsync("P", version, Cancellation)),
        };

        foreach (var error in errors)
        {
            Assert.Contains("'second'", error.Message);
            Assert.Contains("default", error.Message);
        }
    }

    [Fact]
    public async Task ServesTheDefaultRepositoryPastTheGuard()
    {
        var database = Create(RepositoryInfo.Default);

        // It gets as far as the (unreachable) table, which is not the repository refusal.
        var error = await Record.ExceptionAsync(() => database.ExistsAsync("P", Cancellation));

        Assert.False(error is NotSupportedException && error.Message.Contains("repository"), error?.Message);
    }

    [Fact]
    public async Task ARepositoryServiceOverANonRelationalDatabaseKnowsOnlyTheDefaultRepository()
    {
        var repositories = new RepositoryService(new NullContext());

        Assert.False(repositories.SupportsMultipleRepositories);
        Assert.Equal(RepositoryInfo.Default, await repositories.FindAsync("default", Cancellation));
        Assert.Null(await repositories.FindAsync("second", Cancellation));
        Assert.Equal(new[] { RepositoryInfo.Default }, await repositories.ListAsync(Cancellation));

        var error = await Assert.ThrowsAsync<NotSupportedException>(
            () => repositories.CreateAsync("second", RepositoryFormat.NuGet, RepositoryKind.Local, Cancellation));
        Assert.Contains("relational", error.Message);

        await repositories.EnsureDefaultAsync(Cancellation);
    }
}
