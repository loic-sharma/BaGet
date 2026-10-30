using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BaGet.Core.Tests.Retention;

/// <summary>The retention rules as a pure function (P6): what each rule selects, how they combine, and what a legal hold keeps.</summary>
public class RetentionEvaluatorTests
{
    private static readonly DateTime Now = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly RepositoryInfo Local = new(2, "libs", RepositoryFormat.NuGet, RepositoryKind.Local);
    private static readonly RepositoryInfo Remote = new(3, "cache", RepositoryFormat.NuGet, RepositoryKind.Remote);
    private static readonly RepositoryInfo Docker = new(4, "reg", RepositoryFormat.Docker, RepositoryKind.Local);

    private static FormatVersion V(string name, string version, int daysOld, int? downloadedDaysAgo = null, long? size = null, bool prerelease = false)
        => new(name, version, Now.AddDays(-daysOld), downloadedDaysAgo == null ? null : Now.AddDays(-downloadedDaysAgo.Value), size, prerelease);

    private static RetentionDecision Evaluate(RetentionPolicy policy, IReadOnlyList<FormatVersion> versions, RepositoryInfo? repository = null, IReadOnlyList<FormatVersion>? unreferenced = null, RetentionEvaluator.HoldSet? holds = null)
        => RetentionEvaluator.Evaluate(policy, repository ?? Local, versions, unreferenced ?? Array.Empty<FormatVersion>(), holds ?? RetentionEvaluator.HoldSet.Empty, Now);

    private static string[] Keys(IEnumerable<RetentionSelection> items) => items.Select(i => i.Name + " " + i.Version).ToArray();

    [Fact]
    public void KeepLastSelectsEveryVersionBeyondTheNewestOfEachArtifact()
    {
        var versions = new[] { V("a", "1", 30), V("a", "2", 20), V("a", "3", 10), V("b", "1", 5) };

        var decision = Evaluate(new RetentionPolicy { KeepLast = 2 }, versions);

        Assert.Equal(new[] { "a 1" }, Keys(decision.Selected));
        Assert.Equal(4, decision.Examined);
    }

    [Fact]
    public void KeepLastBreaksATieByTheVersionTextSoTheAnswerIsStable()
    {
        var versions = new[] { V("a", "1", 10), V("a", "2", 10), V("a", "3", 10) };

        var decision = Evaluate(new RetentionPolicy { KeepLast = 1 }, versions);

        Assert.Equal(new[] { "a 1", "a 2" }, Keys(decision.Selected));
    }

    [Fact]
    public void OlderThanSelectsByPublishTime()
    {
        var versions = new[] { V("a", "1", 100), V("a", "2", 31), V("a", "3", 30), V("a", "4", 1) };

        var decision = Evaluate(new RetentionPolicy { OlderThanDays = 30 }, versions);

        Assert.Equal(new[] { "a 1", "a 2" }, Keys(decision.Selected));
    }

    [Fact]
    public void UnusedCountsFromThePublishTimeWhenNeverDownloaded()
    {
        var versions = new[]
        {
            V("a", "never-old", 100),
            V("a", "never-young", 5),
            V("a", "used-recently", 100, downloadedDaysAgo: 2),
            V("a", "used-long-ago", 100, downloadedDaysAgo: 60),
        };

        var decision = Evaluate(new RetentionPolicy { UnusedDays = 30 }, versions);

        Assert.Equal(new[] { "a never-old", "a used-long-ago" }, Keys(decision.Selected).OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void UnusedNeverJudgesAPeriodBeforeThePolicyExisted()
    {
        // An upgraded install has no download history: a version with none recorded is not "unused" for days the policy did not watch.
        var versions = new[]
        {
            V("a", "never-old", 100),
            V("a", "used-long-ago", 100, downloadedDaysAgo: 60),
        };

        var fresh = Evaluate(new RetentionPolicy { UnusedDays = 30, Created = Now.AddDays(-1) }, versions);
        var matured = Evaluate(new RetentionPolicy { UnusedDays = 30, Created = Now.AddDays(-31) }, versions);

        Assert.Equal(new[] { "a used-long-ago" }, Keys(fresh.Selected));
        Assert.Equal(new[] { "a never-old", "a used-long-ago" }, Keys(matured.Selected).OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void UnusedWatchesFromUnusedSinceWhenSetEvenOnAnOldPolicy()
    {
        var versions = new[] { V("a", "never-old", 100) };

        var old = Evaluate(new RetentionPolicy { UnusedDays = 30, Created = Now.AddDays(-365), UnusedSince = Now.AddDays(-1) }, versions);
        var legacy = Evaluate(new RetentionPolicy { UnusedDays = 30, Created = Now.AddDays(-365) }, versions);

        Assert.Empty(old.Selected);
        Assert.Equal(new[] { "a never-old" }, Keys(legacy.Selected));
    }

    [Fact]
    public void RulesCombineAsAUnionAndEachVersionNamesEveryRuleThatTookIt()
    {
        var versions = new[] { V("a", "1", 100), V("a", "2", 50), V("a", "3", 1) };

        var decision = Evaluate(new RetentionPolicy { KeepLast = 2, OlderThanDays = 60 }, versions);

        var one = Assert.Single(decision.Selected);
        Assert.Equal("1", one.Version);
        Assert.Equal(new[] { RetentionRule.KeepLast, RetentionRule.OlderThan }, one.Rules);
    }

    [Fact]
    public void PrereleaseOnlyRestrictsTheAgeAndCountRulesToPrereleaseVersions()
    {
        var versions = new[]
        {
            V("a", "1.0.0", 100),
            V("a", "1.1.0-beta", 90, prerelease: true),
            V("a", "1.2.0-beta", 80, prerelease: true),
            V("a", "1.3.0-beta", 70, prerelease: true),
        };

        var decision = Evaluate(new RetentionPolicy { PrereleaseOnly = true, KeepLast = 1, OlderThanDays = 85 }, versions);

        Assert.Equal(new[] { "a 1.1.0-beta", "a 1.2.0-beta" }, Keys(decision.Selected));
        Assert.DoesNotContain(decision.Selected, s => s.Version == "1.0.0");
    }

    [Fact]
    public void NamePatternsLimitWhatThePolicyLooksAt()
    {
        var versions = new[] { V("Contoso.A", "1", 100), V("Contoso.A", "2", 1), V("Other", "1", 100), V("Other", "2", 1) };

        var decision = Evaluate(new RetentionPolicy { KeepLast = 1, NamePatterns = "contoso.*" }, versions);

        Assert.Equal(new[] { "Contoso.A 1" }, Keys(decision.Selected));
        Assert.Equal(2, decision.Examined);
    }

    [Fact]
    public void ALegalHoldKeepsAVersionOrAWholeArtifactAndTheReportSaysSo()
    {
        var versions = new[] { V("a", "1", 100), V("a", "2", 90), V("b", "1", 100), V("b", "2", 90) };
        var holds = new RetentionEvaluator.HoldSet(new[] { ("A", "1"), ("b", string.Empty) });

        var decision = Evaluate(new RetentionPolicy { OlderThanDays = 1 }, versions, holds: holds);

        Assert.Equal(new[] { "a 2" }, Keys(decision.Selected));
        Assert.Equal(new[] { "a 1", "b 1", "b 2" }, Keys(decision.Held));
    }

    [Fact]
    public void TheCacheLimitEvictsTheLeastRecentlyUsedUntilItFits()
    {
        var versions = new[]
        {
            V("a", "1", 10, downloadedDaysAgo: 9, size: 100),
            V("a", "2", 10, downloadedDaysAgo: 1, size: 100),
            V("b", "1", 10, size: 100),
            V("c", "1", 1, downloadedDaysAgo: 0, size: 100),
        };

        var decision = Evaluate(new RetentionPolicy { CacheLimitBytes = 250 }, versions, Remote);

        // 400 bytes held, 250 allowed: two go. b 1 was never used (its publish time is the older), then a 1.
        Assert.Equal(new[] { "a 1", "b 1" }, Keys(decision.Selected));
        Assert.All(decision.Selected, s => Assert.Equal(new[] { RetentionRule.CacheLimit }, s.Rules));
    }

    [Fact]
    public void TheCacheLimitCountsWhatOtherRulesAlreadyTakeAndNeverEvictsAHeldVersion()
    {
        var versions = new[] { V("a", "1", 100, size: 100), V("a", "2", 5, size: 100), V("a", "3", 4, size: 100) };
        var holds = new RetentionEvaluator.HoldSet(new[] { ("a", "2") });

        var decision = Evaluate(new RetentionPolicy { OlderThanDays = 50, CacheLimitBytes = 150 }, versions, Remote, holds: holds);

        // a 1 goes by age (200 left), a 2 is held, so a 3 is the only one the cache rule can evict.
        Assert.Equal(new[] { "a 1", "a 3" }, Keys(decision.Selected));
        Assert.DoesNotContain(decision.Selected, s => s.Version == "2");
    }

    [Fact]
    public void TheCacheLimitIsIgnoredOutsideRemoteRepositories()
    {
        var decision = Evaluate(new RetentionPolicy { CacheLimitBytes = 1 }, new[] { V("a", "1", 1, size: 100) }, Local);

        Assert.Empty(decision.Selected);
        Assert.Contains(decision.Notes, n => n.Contains("remote"));
    }

    [Fact]
    public void UntaggedManifestsOlderThanTheGraceAreSelectedOnlyInDockerRepositories()
    {
        var unreferenced = new[] { V("team/app", "sha256:old", 10), V("team/app", "sha256:new", 1) };
        var policy = new RetentionPolicy { UntaggedAfterDays = 7 };

        var docker = Evaluate(policy, Array.Empty<FormatVersion>(), Docker, unreferenced);
        var nuget = Evaluate(policy, Array.Empty<FormatVersion>(), Local, unreferenced);

        var old = Assert.Single(docker.Selected);
        Assert.Equal("sha256:old", old.Version);
        Assert.True(old.Unreferenced);
        Assert.Empty(nuget.Selected);
        Assert.Contains(nuget.Notes, n => n.Contains("Docker"));
    }

    [Fact]
    public void AnImageHoldProtectsItsUntaggedManifests()
    {
        var unreferenced = new[] { V("team/app", "sha256:old", 10) };
        var holds = new RetentionEvaluator.HoldSet(new[] { ("team/app", string.Empty) });

        var decision = Evaluate(new RetentionPolicy { UntaggedAfterDays = 1 }, Array.Empty<FormatVersion>(), Docker, unreferenced, holds);

        Assert.Empty(decision.Selected);
        Assert.Single(decision.Held);
    }

    [Fact]
    public void ThePlanIsDeterministic()
    {
        var versions = Enumerable.Range(1, 20).Select(i => V("pkg" + (i % 3), i.ToString(), 100 - i)).ToList();
        var policy = new RetentionPolicy { KeepLast = 2, OlderThanDays = 90 };

        var first = Evaluate(policy, versions);
        var second = Evaluate(policy, Enumerable.Reverse(versions).ToList());

        Assert.Equal(Keys(first.Selected), Keys(second.Selected));
    }

    [Fact]
    public void ARunIsDueWhenTheScheduleHasElapsedSinceTheLaterOfTheLastRunAndTheSchedulePoint()
    {
        var policy = new RetentionPolicy { Enabled = true, ScheduleHours = 6, Created = Now.AddDays(-5), ScheduleStart = Now.AddHours(-7) };

        Assert.True(RetentionWorker.IsDue(policy, Now));

        policy.LastRun = Now.AddHours(-2);
        Assert.False(RetentionWorker.IsDue(policy, Now));

        policy.LastRun = Now.AddHours(-6);
        Assert.True(RetentionWorker.IsDue(policy, Now));

        policy.Enabled = false;
        Assert.False(RetentionWorker.IsDue(policy, Now));

        policy.Enabled = true;
        policy.ScheduleHours = null;
        Assert.False(RetentionWorker.IsDue(policy, Now));
    }
}
