using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BaGet.Core.Tests.Upstream;

/// <summary>
/// Parses the document nuget.org publishes at downloads.v1.json. Its shape is an array of
/// records, each one the package id followed by a pair per version:
///
///     [ ["Package.Id", ["1.0.0", 42], ["2.0.0", 7]], ... ]
/// </summary>
public class PackageDownloadsJsonSourceTests
{
    /// <summary>
    /// Serves a fixed body, so the parser can be driven without a network.
    /// </summary>
    private class StubHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(_body)),
            });
        }
    }

    private static Task<Dictionary<string, Dictionary<string, long>>> ParseAsync(string document)
    {
        var client = new HttpClient(new StubHandler(document));
        var target = new PackageDownloadsJsonSource(
            client,
            NullLogger<PackageDownloadsJsonSource>.Instance);

        return target.GetPackageDownloadsAsync();
    }

    [Fact]
    public async Task ReadsAPackagesVersionsAndCounts()
    {
        var result = await ParseAsync(@"[[""Package.Id"",[""1.0.0"",42],[""2.0.0"",7]]]");

        var package = Assert.Single(result);
        Assert.Equal("package.id", package.Key);
        Assert.Equal(42, package.Value["1.0.0"]);
        Assert.Equal(7, package.Value["2.0.0"]);
    }

    [Fact]
    public async Task LowercasesTheIdAndTheVersion()
    {
        // The importer looks packages up by their lowercased id.
        var result = await ParseAsync(@"[[""Package.ID"",[""1.0.0-BETA"",1]]]");

        Assert.Equal("package.id", Assert.Single(result).Key);
        Assert.True(result["package.id"].ContainsKey("1.0.0-beta"));
    }

    [Fact]
    public async Task NormalizesTheVersion()
    {
        var result = await ParseAsync(@"[[""Package.Id"",[""1.0.0.0"",5]]]");

        Assert.True(result["package.id"].ContainsKey("1.0.0"));
    }

    [Fact]
    public async Task ReadsEveryRecord()
    {
        var result = await ParseAsync(
            @"[[""First"",[""1.0.0"",1]],[""Second"",[""1.0.0"",2]],[""Third"",[""1.0.0"",3]]]");

        Assert.Equal(new[] { "first", "second", "third" }, result.Keys.OrderBy(k => k));
    }

    [Fact]
    public async Task ReadsADownloadCountBeyondTheRangeOfAnInt()
    {
        // The contract is a long. A package can plausibly pass two billion downloads, and
        // the whole document is rejected if one entry overflows.
        var count = (long)int.MaxValue + 1000;

        var result = await ParseAsync(
            @"[[""Package.Id"",[""1.0.0""," + count.ToString(CultureInfo.InvariantCulture) + "]]]");

        Assert.Equal(count, result["package.id"]["1.0.0"]);
    }

    [Fact]
    public async Task AnEmptyDocumentYieldsNothing()
    {
        Assert.Empty(await ParseAsync("[]"));
    }

    [Fact]
    public async Task ARecordWhoseSecondEntryIsNotAnArrayIsSkipped()
    {
        // Guards the existing check against malformed entries.
        var result = await ParseAsync(@"[[""Package.Id"",""not-an-array""]]");

        Assert.DoesNotContain("package.id", result.Keys);
    }

    [Fact]
    public async Task AVersionlessRecordYieldsAnEmptyMap()
    {
        var result = await ParseAsync(@"[[""Package.Id""]]");

        Assert.Empty(result["package.id"]);
    }

    [Fact]
    public async Task LaterVersionsOfTheSameIdAreCombined()
    {
        var result = await ParseAsync(
            @"[[""Package.Id"",[""1.0.0"",1]],[""Package.Id"",[""2.0.0"",2]]]");

        var package = Assert.Single(result);
        Assert.Equal(2, package.Value.Count);
    }

    [Fact]
    public async Task DoesNotLeaveItsTemporaryFileBehind()
    {
        // The document is spooled to disk rather than held in memory, so the file has to
        // go when the stream is disposed. The spool file itself is checked: counting the
        // temporary directory's files races with every other test that creates one.
        var target = new PackageDownloadsJsonSource(
            new HttpClient(new StubHandler(@"[[""Package.Id"",[""1.0.0"",1]]]")),
            NullLogger<PackageDownloadsJsonSource>.Instance);

        string path;
        using (var stream = await target.GetDownloadsStreamAsync())
        {
            path = Assert.IsType<FileStream>(stream).Name;
            Assert.True(File.Exists(path));
        }

        Assert.False(File.Exists(path), $"{path} was left behind");
    }
}