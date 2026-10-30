using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Xunit;

namespace BaGet.Core.Tests.Scanning;

/// <summary>
/// The one test that talks to the real OSV.dev API. It is skipped when the API cannot be reached (no internet, a proxy), so the
/// suite never needs the network; the other scanning tests run against a fake OSV.
/// </summary>
public class OsvLiveTests
{
    private sealed class Options : IOptionsSnapshot<ScanningOptions>
    {
        public ScanningOptions Value { get; } = new() { OsvTimeout = TimeSpan.FromSeconds(15) };

        public ScanningOptions Get(string? name) => Value;
    }

    [Fact]
    public async Task AKnownVulnerablePackageVersionIsFlaggedByTheRealApi()
    {
        var source = new OsvVulnerabilitySource(new Options(), new OsvState());
        var component = new ScanComponent("npm", "lodash", "4.17.15");

        System.Collections.Generic.IReadOnlyDictionary<ScanComponent, System.Collections.Generic.IReadOnlyList<VulnerabilityHit>> answers;
        try
        {
            answers = await source.QueryAsync(new[] { component }, TestContext.Current.CancellationToken);
        }
        catch (ScanUnavailableException e) when (e.InnerException is HttpRequestException || e.Message.Contains("did not answer"))
        {
            Assert.Skip("api.osv.dev is not reachable from here: " + e.Message);
            return;
        }

        var hits = answers[component];
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.StartsWith("http", h.Url));
        Assert.Contains(hits, h => h.Severity >= 2);
        Assert.True(hits.Select(h => h.Id).Distinct().Count() == hits.Count);
    }
}
