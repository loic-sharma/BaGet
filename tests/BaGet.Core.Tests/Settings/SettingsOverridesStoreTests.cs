using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BaGet.Core.Tests.Settings;

public class SettingsOverridesStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BaGetSettingsTests", Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly SettingsOverridesStore _store;

    public SettingsOverridesStoreTests()
    {
        _path = Path.Combine(_directory, "nested", "appsettings.Overrides.json");
        _store = new SettingsOverridesStore(_path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static SettingDescriptor Descriptor(string key, SettingKind kind = SettingKind.String)
        => new(key, key.Split(':')[0], "Test", kind, typeof(string), Array.Empty<string>(), false, false, string.Empty);

    private Task Write(params (string Key, object Value)[] changes)
        => _store.WriteAsync(changes.ToDictionary(c => Descriptor(c.Key), c => c.Value), CancellationToken.None);

    [Fact]
    public void MissingFileReadsEmpty()
    {
        Assert.Empty(_store.Read());
    }

    [Fact]
    public async Task WritesNestedKeysAndMergesWithExistingOnes()
    {
        await Write(("Email:Smtp:Host", "mail.example.com"), ("ApiKey", "k"));
        await Write(("Email:Smtp:Port", 25L));

        var values = _store.Read();

        Assert.Equal("mail.example.com", values["Email:Smtp:Host"]);
        Assert.Equal("25", values["email:smtp:port"]);
        Assert.Equal("k", values["ApiKey"]);
    }

    [Fact]
    public async Task KeysMatchCaseInsensitively()
    {
        await Write(("Email:Smtp:Host", "a"));
        await Write(("email:SMTP:host", "b"));

        var json = JsonNode.Parse(File.ReadAllText(_path))?.AsObject();

        #pragma warning disable CS8604
        Assert.Single(json);
        #pragma warning restore CS8604
        Assert.Equal("b", _store.Read()["Email:Smtp:Host"]);
    }

    [Fact]
    public async Task RemovingTheLastKeyPrunesEmptyParents()
    {
        await Write(("Email:Smtp:Host", "a"), ("ApiKey", "k"));

        #pragma warning disable CS8620
        await Write(("Email:Smtp:Host", null));
        #pragma warning restore CS8620

        var json = JsonNode.Parse(File.ReadAllText(_path))?.AsObject();
        #pragma warning disable CS8604
        Assert.Equal(new[] { "ApiKey" }, json.Select(p => p.Key));
        #pragma warning restore CS8604
    }

    [Fact]
    public async Task ARemovedKeyThatIsAbsentIsIgnored()
    {
        #pragma warning disable CS8620
        await Write(("Not:There", null));
        #pragma warning restore CS8620

        Assert.Empty(_store.Read());
    }

    [Fact]
    public async Task AShorterListLeavesNoStaleEntries()
    {
        await Write(("Site:Names", new[] { "a", "b", "c" }));
        await Write(("Site:Names", new[] { "x" }));

        var values = _store.Read();

        Assert.Equal("x", values["Site:Names:0"]);
        Assert.False(values.ContainsKey("Site:Names:1"));
        Assert.False(values.ContainsKey("Site:Names:2"));
    }

    [Fact]
    public async Task ValuesAreWrittenAsTypedJson()
    {
        await Write(
            ("Flag", true),
            ("Count", 7L),
            ("Ratio", 1.5d),
            ("Span", TimeSpan.FromMinutes(10)),
            ("Items", new[] { "a", "b" }),
            ("Text", "t"));

        var json = JsonNode.Parse(File.ReadAllText(_path))?.AsObject();

        #pragma warning disable CS8602, CS8604
        Assert.True((bool)json["Flag"]);
        #pragma warning restore CS8602
        Assert.Equal(7L, (long)json["Count"]);
        Assert.Equal(1.5d, (double)json["Ratio"]);
        #pragma warning restore CS8604
        Assert.Equal("00:10:00", (string?)json["Span"]);
        Assert.Equal(2, json["Items"]?.AsArray().Count);
        Assert.Equal("t", (string?)json["Text"]);
    }

    [Fact]
    public async Task WritingLeavesNoTemporaryFileBehind()
    {
        await Write(("A", "1"));
        await Write(("A", "2"));

        #pragma warning disable CS8604
        var files = Directory.GetFiles(Path.GetDirectoryName(_path), "*", SearchOption.AllDirectories);
        #pragma warning restore CS8604

        Assert.Equal(new[] { _path }, files);
    }

    [Fact]
    public async Task CommentsAndTrailingCommasInTheFileAreTolerated()
    {
        #pragma warning disable CS8604
        Directory.CreateDirectory(Path.GetDirectoryName(_path));
        #pragma warning restore CS8604
        File.WriteAllText(_path, "{ // note\n \"A\": { \"B\": 1, }, }");

        Assert.Equal("1", _store.Read()["A:B"]);

        await Write(("A:C", "x"));

        Assert.Equal("1", _store.Read()["A:B"]);
        Assert.Equal("x", _store.Read()["A:C"]);
    }

    [Fact]
    public void AnInvalidFileIsReportedWithItsPath()
    {
        #pragma warning disable CS8604
        Directory.CreateDirectory(Path.GetDirectoryName(_path));
        #pragma warning restore CS8604
        File.WriteAllText(_path, "{ not json");

        var error = Assert.Throws<InvalidOperationException>(() => _store.Read());

        Assert.Contains(_path, error.Message);
    }

    [Fact]
    public void ResolvePathUsesTheConfiguredPathOrTheDefaultFileUnderTheBase()
    {
        var empty = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        #pragma warning disable CS8620
        var configured = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { [SettingsOverridesStore.PathKey] = "conf/x.json" })
            .Build();
        #pragma warning restore CS8620

        Assert.Equal(
            Path.GetFullPath(Path.Combine(_directory, SettingsOverridesStore.DefaultFileName)),
            SettingsOverridesStore.ResolvePath(empty, _directory));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(_directory, "conf", "x.json")),
            SettingsOverridesStore.ResolvePath(configured, _directory));
    }
}
