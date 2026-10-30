using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Core.Tests.Formats;

/// <summary>
/// The parser of an <c>npm publish</c> body: JSON with the tarball inline as base64, read without holding the tarball in memory,
/// whatever the JSON encoder that wrote it escaped.
/// </summary>
public class NpmPublishBodyTests
{
    private static Stream Body(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    private static string Document(byte[] tarball, string? extraManifest = null, string attachmentName = "left-pad-1.0.0.tgz", string? data = null)
        => "{\"name\":\"left-pad\",\"readme\":\"# hi\"," + (extraManifest ?? "") + "\"_attachments\":{\"" + attachmentName + "\":{\"content_type\":\"application/octet-stream\",\"data\":\""
           + (data ?? Convert.ToBase64String(tarball)) + "\",\"length\":" + tarball.Length + "}}}";

    private static byte[] Bytes(int length, int seed = 7)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static async Task<(byte[] Content, long Length)> ReadAsync(NpmAttachment attachment)
    {
        await using var stream = attachment.Open();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, TestContext.Current.CancellationToken);
        return (buffer.ToArray(), attachment.Length);
    }

    [Fact]
    public async Task TheAttachmentIsDecodedToAFileAndTheJsonKeepsEverythingElse()
    {
        var tarball = Bytes(1000);

        using var document = await NpmPublishBody.ParseAsync(Body(Document(tarball)), 0, TestContext.Current.CancellationToken);

        Assert.Equal("left-pad", (string?)document.Root["name"]);
        Assert.Equal("# hi", (string?)document.Root["readme"]);
        var attachment = Assert.Single(document.Attachments);
        Assert.Equal("left-pad-1.0.0.tgz", attachment.Name);
        Assert.Equal("application/octet-stream", attachment.ContentType);
        Assert.Equal(1000, attachment.DeclaredLength);
        var (content, length) = await ReadAsync(attachment);
        Assert.Equal(tarball, content);
        Assert.Equal(1000, length);
        // The data of the attachment was taken out of the document, not kept in memory.
        Assert.Equal(string.Empty, (string?)document.Root["_attachments"]![attachment.Name]!["data"]);
    }

    [Fact]
    public async Task ABodyWithoutAttachmentsHasNone()
    {
        using var document = await NpmPublishBody.ParseAsync(Body("{\"name\":\"x\",\"versions\":{\"1.0.0\":{\"deprecated\":\"old\"}}}"), 0, TestContext.Current.CancellationToken);

        Assert.Empty(document.Attachments);
        Assert.Equal("old", (string?)document.Root["versions"]!["1.0.0"]!["deprecated"]);
    }

    [Fact]
    public async Task TextThatLooksLikeStructureInsideOtherStringsDoesNotConfuseTheScan()
    {
        var tarball = Bytes(300);
        var tricky = "\"description\":\"a \\\"data\\\": \\\"AAAA\\\" and {\\\"_attachments\\\":{}} and a brace } and [\",\"readme2\":\"\\\\\\\\\",";

        using var document = await NpmPublishBody.ParseAsync(Body(Document(tarball, tricky)), 0, TestContext.Current.CancellationToken);

        Assert.Equal("a \"data\": \"AAAA\" and {\"_attachments\":{}} and a brace } and [", (string?)document.Root["description"]);
        Assert.Equal("\\\\", (string?)document.Root["readme2"]);
        Assert.Equal(tarball, (await ReadAsync(Assert.Single(document.Attachments))).Content);
    }

    [Fact]
    public async Task OnlyTheRootsAttachmentsAreTarballsAndAFieldCalledDataElsewhereIsKept()
    {
        var tarball = Bytes(64);
        var nested = "\"versions\":{\"1.0.0\":{\"_attachments\":{\"x\":{\"data\":\"keep me\"}},\"data\":\"also kept\"}},";

        using var document = await NpmPublishBody.ParseAsync(Body(Document(tarball, nested)), 0, TestContext.Current.CancellationToken);

        Assert.Equal("keep me", (string?)document.Root["versions"]!["1.0.0"]!["_attachments"]!["x"]!["data"]);
        Assert.Equal("also kept", (string?)document.Root["versions"]!["1.0.0"]!["data"]);
        Assert.Single(document.Attachments);
    }

    [Fact]
    public async Task EscapesAJsonEncoderWroteInsideTheBase64AreUndone()
    {
        // Bytes whose base64 holds '+' and '/' in plenty.
        var tarball = Enumerable.Range(0, 3000).Select(i => (byte)(i % 2 == 0 ? 0xfb : 0xff)).ToArray();
        var plain = Convert.ToBase64String(tarball);
        Assert.Contains('+', plain);
        Assert.Contains('/', plain);

        var escapedSlash = plain.Replace("/", "\\/");
        var unicode = plain.Replace("+", "\\u002B").Replace("=", "\\u003d");
        var wrapped = string.Join("\\n", Enumerable.Range(0, (plain.Length + 75) / 76).Select(i => plain.Substring(i * 76, Math.Min(76, plain.Length - i * 76))));

        foreach (var data in new[] { escapedSlash, unicode, wrapped })
        {
            using var document = await NpmPublishBody.ParseAsync(Body(Document(tarball, data: data)), 0, TestContext.Current.CancellationToken);
            Assert.Equal(tarball, (await ReadAsync(Assert.Single(document.Attachments))).Content);
        }
    }

    [Fact]
    public async Task AnAttachmentNameThatAnEncoderEscapedIsMatchedByItsDecodedName()
    {
        var tarball = Bytes(40);

        using var document = await NpmPublishBody.ParseAsync(Body(Document(tarball, attachmentName: "left-pad-1.0.0\\u002Bbuild.tgz")), 0, TestContext.Current.CancellationToken);

        var attachment = Assert.Single(document.Attachments);
        Assert.Equal("left-pad-1.0.0+build.tgz", attachment.Name);
        Assert.Equal(40, attachment.DeclaredLength);
    }

    [Fact]
    public async Task ABodyOverTheLimitIsRefusedAndLeavesNoTemporaryFile()
    {
        var before = TempFiles();

        await Assert.ThrowsAsync<NpmBodyTooLargeException>(() => NpmPublishBody.ParseAsync(Body(Document(Bytes(5000))), 1000, TestContext.Current.CancellationToken));

        Assert.Equal(before, TempFiles());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"_attachments\":{\"a.tgz\":{\"data\":\"AAAA")]
    [InlineData("{\"a\":")]
    public async Task ABodyThatIsNotAPublishDocumentIsRefused(string body)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => NpmPublishBody.ParseAsync(Body(body), 0, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("AAA")]
    [InlineData("AA*A")]
    [InlineData("AAAA\\q")]
    [InlineData("AAAA\\u00")]
    [InlineData("AAAA\\u00zz")]
    [InlineData("\\u00e9AAA")]
    public async Task AnAttachmentThatIsNotBase64IsRefusedAndLeavesNoTemporaryFile(string data)
    {
        var before = TempFiles();

        await Assert.ThrowsAsync<InvalidDataException>(() => NpmPublishBody.ParseAsync(Body(Document(Bytes(3), data: data)), 0, TestContext.Current.CancellationToken));

        Assert.Equal(before, TempFiles());
    }

    [Fact]
    public async Task DisposingTheDocumentDeletesTheDecodedFiles()
    {
        var before = TempFiles();
        var document = await NpmPublishBody.ParseAsync(Body(Document(Bytes(100))), 0, TestContext.Current.CancellationToken);
        var path = Assert.Single(document.Attachments).Path;
        Assert.True(File.Exists(path));

        document.Dispose();

        Assert.False(File.Exists(path));
        Assert.Equal(before, TempFiles());
    }

    private static int TempFiles() => Directory.GetFiles(Path.GetTempPath(), "baget-npm-*.tmp").Length;
}

/// <summary>Reading <c>package.json</c> out of an npm tarball.</summary>
public class NpmTarballReaderTests
{
    private static MemoryStream Archive(params (string Path, string Content)[] files)
    {
        var raw = new MemoryStream();
        using (var gzip = new GZipStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, path) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) });
            }
        }

        raw.Position = 0;
        return raw;
    }

    [Fact]
    public async Task ThePackageJsonOfTheTopFolderIsRead()
    {
        using var stream = Archive(("package/index.js", "1"), ("package/package.json", "{\"name\":\"left-pad\",\"version\":\"1.0.0\"}"));

        var json = await NpmTarballReader.ReadPackageJsonAsync(stream, TestContext.Current.CancellationToken);

        Assert.Equal("left-pad", (string?)json!["name"]);
    }

    [Fact]
    public async Task AnyTopFolderNameWorksAndANestedPackageJsonIsIgnored()
    {
        using var stream = Archive(("package/node_modules/x/package.json", "{\"name\":\"x\"}"), ("src/package.json", "{\"name\":\"y\"}"));

        var json = await NpmTarballReader.ReadPackageJsonAsync(stream, TestContext.Current.CancellationToken);

        Assert.Equal("y", (string?)json!["name"]);
    }

    [Fact]
    public async Task NothingIsReadFromAnArchiveWithoutOneOrFromSomethingThatIsNotAnArchive()
    {
        using var without = Archive(("package/index.js", "1"));
        using var notJson = Archive(("package/package.json", "not json"));
        using var notAnObject = Archive(("package/package.json", "[1]"));
        using var text = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));
        using var gzipOnly = new MemoryStream();
        await using (var gzip = new GZipStream(gzipOnly, CompressionLevel.Fastest, leaveOpen: true))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes("not a tar"), TestContext.Current.CancellationToken);
        }

        gzipOnly.Position = 0;

        Assert.Null(await NpmTarballReader.ReadPackageJsonAsync(without, TestContext.Current.CancellationToken));
        Assert.Null(await NpmTarballReader.ReadPackageJsonAsync(notJson, TestContext.Current.CancellationToken));
        Assert.Null(await NpmTarballReader.ReadPackageJsonAsync(notAnObject, TestContext.Current.CancellationToken));
        Assert.Null(await NpmTarballReader.ReadPackageJsonAsync(text, TestContext.Current.CancellationToken));
        Assert.Null(await NpmTarballReader.ReadPackageJsonAsync(gzipOnly, TestContext.Current.CancellationToken));
    }
}

/// <summary>The rules for names, versions, tags and file names.</summary>
public class NpmNamesTests
{
    [Theory]
    [InlineData("left-pad", "left-pad")]
    [InlineData("@acme/tool", "@acme/tool")]
    [InlineData("@acme%2ftool", "@acme/tool")]
    [InlineData("@acme%2Ftool", "@acme/tool")]
    [InlineData("a", "a")]
    [InlineData("jquery.ui_2~x", "jquery.ui_2~x")]
    [InlineData("@a.b/c-d", "@a.b/c-d")]
    public void ValidNamesAreAccepted(string name, string expected)
    {
        Assert.True(NpmNames.TryParse(name, out var normalized, out _));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Left-Pad")]
    [InlineData("_hidden")]
    [InlineData(".hidden")]
    [InlineData("-dash")]
    [InlineData("node_modules")]
    [InlineData("favicon.ico")]
    [InlineData("has space")]
    [InlineData("@/x")]
    [InlineData("@acme/")]
    [InlineData("@acme")]
    [InlineData("@acme/a/b")]
    [InlineData("@Acme/tool")]
    [InlineData("@acme/_x")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("a%2f..%2fb")]
    [InlineData("%zz")]
    public void InvalidNamesAreRefused(string name)
    {
        Assert.False(NpmNames.TryParse(name, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void ANameLongerThanNpmAllowsIsRefused()
    {
        Assert.True(NpmNames.TryParse(new string('a', 214), out _, out _));
        Assert.False(NpmNames.TryParse(new string('a', 215), out _, out _));
    }

    [Fact]
    public void TarballNamesAndPathsDropTheScopeFromTheFileAndKeepItInTheFolder()
    {
        Assert.Equal("tool-1.0.0.tgz", NpmNames.TarballFileName("@acme/tool", "1.0.0"));
        Assert.Equal("npm/@acme/tool/-/tool-1.0.0.tgz", NpmNames.TarballPath("@acme/tool", "1.0.0"));
        Assert.Equal("npm/left-pad/-/left-pad-1.0.0-beta.1.tgz", NpmNames.TarballPath("left-pad", "1.0.0-beta.1"));
        Assert.Equal("@acme", NpmNames.Scope("@acme/tool"));
        Assert.Null(NpmNames.Scope("left-pad"));
        Assert.Equal("tool", NpmNames.Unscoped("@acme/tool"));
    }

    [Theory]
    [InlineData("left-pad", "left-pad-1.0.0.tgz", "1.0.0")]
    [InlineData("left-pad", "left-pad-1.0.0-beta.2+build.5.tgz", "1.0.0-beta.2+build.5")]
    [InlineData("@acme/tool", "tool-2.1.3.tgz", "2.1.3")]
    public void TheVersionIsReadBackFromATarballFileName(string name, string file, string version)
    {
        Assert.True(NpmNames.TryParseTarballFile(name, file, out var parsed));
        Assert.Equal(version, parsed);
    }

    [Theory]
    [InlineData("left-pad", "right-pad-1.0.0.tgz")]
    [InlineData("left-pad", "left-pad-1.0.tgz")]
    [InlineData("left-pad", "left-pad-1.0.0.zip")]
    [InlineData("left-pad", "left-pad-1.0.0")]
    [InlineData("left-pad", "left-pad-..%2f1.0.0.tgz")]
    [InlineData("left-pad", null)]
    public void AFileNameThatIsNotTheTarballOfThePackageIsRefused(string name, string? file)
    {
        Assert.False(NpmNames.TryParseTarballFile(name, file, out _));
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("1.0.0-rc.1", true)]
    [InlineData("1.0.0+build", true)]
    [InlineData("0.0.1", true)]
    [InlineData("1.0", false)]
    [InlineData("v1.0.0", false)]
    [InlineData(" 1.0.0", false)]
    [InlineData("1.0.0.0", false)]
    [InlineData("latest", false)]
    [InlineData("", false)]
    public void OnlySemanticVersionsInTheirCanonicalFormAreVersions(string version, bool valid)
    {
        Assert.Equal(valid, NpmNames.TryParseVersion(version, out _));
    }

    [Fact]
    public void BuildMetadataDoesNotMakeAVersionDifferent()
    {
        Assert.Equal(NpmNames.Comparable("1.0.0"), NpmNames.Comparable("1.0.0+abc"));
        Assert.NotEqual(NpmNames.Comparable("1.0.0"), NpmNames.Comparable("1.0.1"));
    }

    [Theory]
    [InlineData("latest", true)]
    [InlineData("next", true)]
    [InlineData("beta-2", true)]
    [InlineData("1.0.0", false)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("a/b", false)]
    public void TagsAreNamesThatAreNotVersions(string tag, bool valid)
    {
        Assert.Equal(valid, NpmNames.IsValidTag(tag, out _));
    }

    [Fact]
    public void LatestPrefersTheHighestStableVersion()
    {
        Assert.Equal("1.2.0", NpmNames.Highest(new[] { "1.0.0", "1.2.0", "2.0.0-beta.1", "1.10.0-rc" }, stableFirst: true));
        Assert.Equal("2.0.0-beta.1", NpmNames.Highest(new[] { "1.0.0", "2.0.0-beta.1" }, stableFirst: false));
        Assert.Equal("1.0.0-alpha", NpmNames.Highest(new[] { "1.0.0-alpha" }));
        Assert.Null(NpmNames.Highest(Array.Empty<string>()));
        Assert.Equal("1.10.0", NpmNames.Highest(new[] { "1.9.0", "1.10.0", "1.2.0" }));
    }
}
