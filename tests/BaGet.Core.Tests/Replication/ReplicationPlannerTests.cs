using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Core.Tests.Replication;

/// <summary>The decision part of a replication run (P9) as a pure function, and the single-node job lease.</summary>
public class ReplicationPlannerTests
{
    private static ReplicaItem I(string name, string version, string digest = "", string part = "") => new(name, version, part, 0, digest);

    private static string[] Keys(System.Collections.Generic.IEnumerable<ReplicaItem> items) => items.Select(i => i.ToString()).ToArray();

    private static ReplicationPlan Plan(ReplicaItem[] source, ReplicaItem[] target, ReplicaItem[]? ledger = null, ReplicationConflict conflict = ReplicationConflict.TargetWins, bool deletes = false, StringComparer? names = null)
        => ReplicationPlanner.Plan(source, target, ledger ?? Array.Empty<ReplicaItem>(), names ?? StringComparer.Ordinal, conflict, deletes);

    [Fact]
    public void WhatTheTargetLacksIsCopiedAndWhatItHoldsIsSkipped()
    {
        var plan = Plan(new[] { I("a", "1"), I("a", "2"), I("b", "1") }, new[] { I("a", "1") });

        Assert.Equal(new[] { "a 2", "b 1" }, Keys(plan.Copy));
        Assert.Equal(1, plan.Skipped);
        Assert.Equal(3, plan.Examined);
        Assert.Empty(plan.Replace);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void ItemsOfDifferentContentAreAConflictTheTargetKeepsByDefaultAndTheSourceReplacesOnRequest()
    {
        var source = new[] { I("a", "1", "sha-x") };
        var target = new[] { I("a", "1", "sha-y") };

        var keep = Plan(source, target);
        Assert.Equal(new[] { "a 1" }, Keys(keep.Conflicts));
        Assert.Empty(keep.Copy);
        Assert.Empty(keep.Replace);

        var replace = Plan(source, target, conflict: ReplicationConflict.SourceWins);
        Assert.Equal(new[] { "a 1" }, Keys(replace.Replace));
        Assert.Empty(replace.Conflicts);
    }

    [Fact]
    public void AMissingDigestOnEitherSideNeverMakesAConflict()
    {
        Assert.Equal(1, Plan(new[] { I("a", "1", "") }, new[] { I("a", "1", "sha-y") }).Skipped);
        Assert.Equal(1, Plan(new[] { I("a", "1", "sha-x") }, new[] { I("a", "1", "") }).Skipped);
    }

    [Fact]
    public void TheNamesOfAFormatCompareTheWayItSaysAndVersionsAndPartsExactly()
    {
        var same = Plan(new[] { I("Contoso.Lib", "1.0.0") }, new[] { I("contoso.lib", "1.0.0") }, names: StringComparer.OrdinalIgnoreCase);
        Assert.Empty(same.Copy);
        Assert.Equal(1, same.Skipped);

        var exact = Plan(new[] { I("Contoso.Lib", "1.0.0") }, new[] { I("contoso.lib", "1.0.0") });
        Assert.Single(exact.Copy);

        var parts = Plan(new[] { I("g:a", "1", part: "a-1.jar"), I("g:a", "1", part: "a-1.pom") }, new[] { I("g:a", "1", part: "a-1.jar") });
        Assert.Equal(new[] { "g:a 1 a-1.pom" }, Keys(parts.Copy));
    }

    [Fact]
    public void ADeleteIsPropagatedOnlyForWhatWasCopiedAndOnlyWhenAsked()
    {
        var ledger = new[] { I("copied", "1") };
        var target = new[] { I("copied", "1"), I("theirs", "1") };

        var off = Plan(Array.Empty<ReplicaItem>(), target, ledger);
        Assert.Empty(off.Delete);
        Assert.Equal(new[] { "copied 1" }, Keys(off.Forget));

        var on = Plan(Array.Empty<ReplicaItem>(), target, ledger, deletes: true);
        Assert.Equal(new[] { "copied 1" }, Keys(on.Delete));
        Assert.Empty(on.Forget);
        Assert.DoesNotContain("theirs 1", Keys(on.Delete));
    }

    [Fact]
    public void AnItemTheTargetChangedAfterItWasCopiedIsNotDeletedUnlessTheSourceWins()
    {
        var ledger = new[] { I("app", "latest", "sha-a") };
        var target = new[] { I("app", "latest", "sha-b") };

        var keep = Plan(Array.Empty<ReplicaItem>(), target, ledger, ReplicationConflict.TargetWins, deletes: true);
        Assert.Empty(keep.Delete);
        Assert.Equal(new[] { "app latest" }, Keys(keep.Forget));

        var win = Plan(Array.Empty<ReplicaItem>(), target, ledger, ReplicationConflict.SourceWins, deletes: true);
        Assert.Equal(new[] { "app latest" }, Keys(win.Delete));
    }

    [Fact]
    public void ALedgerEntryGoneFromBothSidesIsForgottenAndOneStillAtTheSourceIsLeftAlone()
    {
        var ledger = new[] { I("gone", "1"), I("kept", "1") };

        var plan = Plan(new[] { I("kept", "1") }, new[] { I("kept", "1") }, ledger, deletes: true);

        Assert.Equal(new[] { "gone 1" }, Keys(plan.Forget));
        Assert.Empty(plan.Delete);
        Assert.Equal(2, plan.Examined);
    }

    [Fact]
    public async Task TheInProcessLeaseIsHeldUntilDisposedAndPerJob()
    {
        var lease = new InProcessJobLease();

        var first = await lease.TryAcquireAsync("replication:1", TestContext.Current.CancellationToken);
        Assert.NotNull(first);
        Assert.Null(await lease.TryAcquireAsync("replication:1", TestContext.Current.CancellationToken));
        var other = await lease.TryAcquireAsync("replication:2", TestContext.Current.CancellationToken);
        Assert.NotNull(other);

        await first!.DisposeAsync();
        await first.DisposeAsync(); // releasing twice is harmless
        var again = await lease.TryAcquireAsync("replication:1", TestContext.Current.CancellationToken);
        Assert.NotNull(again);
        Assert.Null(await lease.TryAcquireAsync("replication:1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ADigestDifferenceIsOnlyAConflictWhenBothSidesKnowOne()
    {
        Assert.True(I("a", "1", "x").DiffersFrom(I("a", "1", "y")));
        Assert.False(I("a", "1", "x").DiffersFrom(I("a", "1", "x")));
        Assert.False(I("a", "1", "x").DiffersFrom(I("a", "1")));
    }

    [Fact]
    public void ATransferFailsOnAPeerFactoryThatCannotParseTheAddress()
    {
        var factory = new ReplicationPeerFactory(new RemoteHttpClientFactory());

        Assert.Throws<ReplicationException>(() => factory.Create("not a url", "default", "key"));
        using var peer = factory.Create("https://peer.example/base", "mirror", "key");
        Assert.Equal("https://peer.example/base/", peer.Server.AbsoluteUri);
        Assert.Equal("r/mirror/", peer.PathPrefix);
        Assert.Equal("https://peer.example/base/r/mirror/v3/index.json", peer.Feed("v3/index.json").AbsoluteUri);
        Assert.Equal("https://peer.example/base/v2/default/img/blobs/sha256:a", peer.Root("v2/default/img/blobs/sha256:a").AbsoluteUri);
    }
}
