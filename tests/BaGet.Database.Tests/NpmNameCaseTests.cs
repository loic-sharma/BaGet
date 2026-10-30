using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BaGet.Database.Tests;

/// <summary>
/// npm package names and dist-tags are case-sensitive (legacy names such as <c>JSONStream</c> still exist), on every engine:
/// SQL Server and MySQL compare text case-insensitively by default, so their npm columns carry a binary collation.
/// </summary>
public class NpmNameCaseTests
{
    public static IEnumerable<object[]> EngineTypes() => Engines.Types.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(EngineTypes))]
    public async Task NamesAndTagsDifferingOnlyInCaseAreDistinctAndLookedUpExactly(string type)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var engine = await Engines.GetOrSkipAsync(type);

        await using var context = (DbContext)engine.Factory.Create(engine.NewConnectionString(), engine.Options);
        var iContext = (IContext)context;
        await iContext.RunMigrationsAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var legacy = new NpmPackage { Name = "JSONStream", Created = now, Modified = now };
        var lower = new NpmPackage { Name = "jsonstream", Created = now, Modified = now };
        iContext.NpmPackages!.AddRange(legacy, lower);
        await iContext.SaveChangesAsync(cancellationToken);

        iContext.NpmDistTags!.AddRange(
            new NpmDistTag { PackageKey = legacy.Key, Tag = "Beta", Version = "1.0.0" },
            new NpmDistTag { PackageKey = legacy.Key, Tag = "beta", Version = "2.0.0" });
        await iContext.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        Assert.Equal(legacy.Key, (await iContext.NpmPackages.SingleAsync(p => p.Name == "JSONStream", cancellationToken)).Key);
        Assert.Equal(lower.Key, (await iContext.NpmPackages.SingleAsync(p => p.Name == "jsonstream", cancellationToken)).Key);
        Assert.Null(await iContext.NpmPackages.FirstOrDefaultAsync(p => p.Name == "JsonStream", cancellationToken));
        Assert.Equal("1.0.0", (await iContext.NpmDistTags.SingleAsync(t => t.PackageKey == legacy.Key && t.Tag == "Beta", cancellationToken)).Version);
        Assert.Equal("2.0.0", (await iContext.NpmDistTags.SingleAsync(t => t.PackageKey == legacy.Key && t.Tag == "beta", cancellationToken)).Version);

        // The unique index still refuses the identical name.
        iContext.NpmPackages.Add(new NpmPackage { Name = "JSONStream", Created = now, Modified = now });
        await Assert.ThrowsAsync<DbUpdateException>(() => iContext.SaveChangesAsync(cancellationToken));
    }
}
