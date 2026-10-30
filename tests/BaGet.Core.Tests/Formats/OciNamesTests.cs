using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace BaGet.Core.Tests;

/// <summary>The naming rules of the OCI Distribution Spec, the path split, the scope strings and the manifest reader.</summary>
public class OciNamesTests
{
    private const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("app", true)]
    [InlineData("team/app", true)]
    [InlineData("a/b/c/d", true)]
    [InlineData("my-app_v2.final", true)]
    [InlineData("a__b", true)]
    [InlineData("App", false)]
    [InlineData("team//app", false)]
    [InlineData("/app", false)]
    [InlineData("app/", false)]
    [InlineData("-app", false)]
    [InlineData("app-", false)]
    [InlineData("a..b", false)]
    [InlineData("a___b", false)]
    [InlineData("", false)]
    [InlineData("app name", false)]
    public void ImageNamesFollowTheSpecification(string name, bool valid)
    {
        Assert.Equal(valid, OciNames.IsValidImage(name));
    }

    [Fact]
    public void AnImageNameIsBoundedByTheColumn()
    {
        Assert.True(OciNames.IsValidImage(new string('a', OciNames.MaxImageLength)));
        Assert.False(OciNames.IsValidImage(new string('a', OciNames.MaxImageLength + 1)));
    }

    [Theory]
    [InlineData("latest", true)]
    [InlineData("v1.2.3-rc.1", true)]
    [InlineData("_x", true)]
    [InlineData("A", true)]
    [InlineData(".x", false)]
    [InlineData("-x", false)]
    [InlineData("", false)]
    [InlineData("a b", false)]
    [InlineData("a/b", false)]
    public void TagsFollowTheSpecification(string tag, bool valid)
    {
        Assert.Equal(valid, OciNames.IsValidTag(tag));
    }

    [Fact]
    public void ATagIsAtMost128Characters()
    {
        Assert.True(OciNames.IsValidTag(new string('a', 128)));
        Assert.False(OciNames.IsValidTag(new string('a', 129)));
    }

    [Theory]
    [InlineData(Digest, true, true)]
    [InlineData("sha256:ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef0123456789", false, true)]
    [InlineData("sha256:abc", false, true)]
    [InlineData("sha512:abcdef", false, true)]
    [InlineData("nodigest", false, false)]
    [InlineData("", false, false)]
    public void DigestsAreSha256WithSixtyFourLowerCaseHexDigits(string digest, bool supported, bool lookslike)
    {
        Assert.Equal(supported, OciNames.IsValidDigest(digest));
        Assert.Equal(lookslike || supported, OciNames.LooksLikeDigest(digest));
    }

    [Fact]
    public void ABlobPathFansOutByTheFirstTwoDigits()
    {
        Assert.Equal("docker/blobs/sha256/01/0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", OciNames.BlobPath(Digest));
        Assert.Throws<ArgumentException>(() => OciNames.BlobPath("sha256:short"));
    }

    [Fact]
    public void TheImageNameIsFoundFromTheRight()
    {
        Assert.Equal(new OciPath(OciPathKind.Blob, "team/app", Digest), OciNames.Parse($"team/app/blobs/{Digest}"));
        Assert.Equal(new OciPath(OciPathKind.Manifest, "a/b/c", "latest"), OciNames.Parse("a/b/c/manifests/latest"));
        Assert.Equal(new OciPath(OciPathKind.Manifest, "app", Digest), OciNames.Parse($"app/manifests/{Digest}"));
        Assert.Equal(new OciPath(OciPathKind.TagList, "team/app", null), OciNames.Parse("team/app/tags/list"));
        Assert.Equal(new OciPath(OciPathKind.Referrers, "team/app", Digest), OciNames.Parse($"team/app/referrers/{Digest}"));
    }

    [Fact]
    public void UploadPathsAreToldApartFromBlobPaths()
    {
        Assert.Equal(new OciPath(OciPathKind.UploadStart, "team/app", null), OciNames.Parse("team/app/blobs/uploads/"));
        Assert.Equal(new OciPath(OciPathKind.UploadStart, "team/app", null), OciNames.Parse("team/app/blobs/uploads"));
        Assert.Equal(new OciPath(OciPathKind.Upload, "team/app", "0123abc-_"), OciNames.Parse("team/app/blobs/uploads/0123abc-_"));
        Assert.Null(OciNames.Parse("team/app/blobs/uploads/bad ref"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("app")]
    [InlineData("app/blobs")]
    [InlineData("App/manifests/latest")]
    [InlineData("app/blobs/notadigest")]
    [InlineData("app/other/latest")]
    [InlineData("app/tags/other")]
    public void ThingsThatAreNotRegistryPathsAreNull(string? path)
    {
        Assert.Null(OciNames.Parse(path));
    }

    [Fact]
    public void AnInvalidManifestReferenceStillParsesSoAReadCanAnswerNotFound()
    {
        Assert.Equal(new OciPath(OciPathKind.Manifest, "app", ".INVALID_MANIFEST_NAME"), OciNames.Parse("app/manifests/.INVALID_MANIFEST_NAME"));
        Assert.Null(OciNames.Parse("app/manifests/" + new string('x', 301)));
    }

    [Fact]
    public void ScopesAreParsedAsDockerSendsThem()
    {
        Assert.Equal(new OciGrant("libs", "team/app", OciActions.Pull | OciActions.Push), OciScopes.Parse("repository:libs/team/app:pull,push"));
        Assert.Equal(new OciGrant("libs", "app", OciActions.Delete), OciScopes.Parse("repository:Libs/app:delete"));
        Assert.Equal(new OciGrant("libs", "app", OciActions.All), OciScopes.Parse("repository:libs/app:*"));
        Assert.Equal(new OciGrant("libs", "app", OciActions.Pull), OciScopes.Parse("repository:libs/app:pull,unknown"));
        Assert.Null(OciScopes.Parse("registry:catalog:*"));
        Assert.Null(OciScopes.Parse("repository:libs:pull"));
        Assert.Null(OciScopes.Parse("repository:/app:pull"));
        Assert.Null(OciScopes.Parse(""));
        Assert.Equal("repository:libs/team/app:pull,push", OciScopes.Format(new OciGrant("libs", "team/app", OciActions.Pull | OciActions.Push)));
    }

    private static byte[] Json(string text) => Encoding.UTF8.GetBytes(text);

    private static string ManifestJson(string config = Digest, string layer = "sha256:1111111111111111111111111111111111111111111111111111111111111111", string extra = "")
        => "{\"schemaVersion\":2,\"mediaType\":\"application/vnd.oci.image.manifest.v1+json\"," +
           $"\"config\":{{\"mediaType\":\"application/vnd.oci.image.config.v1+json\",\"digest\":\"{config}\",\"size\":10}}," +
           $"\"layers\":[{{\"mediaType\":\"application/vnd.oci.image.layer.v1.tar+gzip\",\"digest\":\"{layer}\",\"size\":20}}]{extra}}}";

    [Fact]
    public void AnImageManifestIsReadWithItsConfigAndLayers()
    {
        var parsed = OciManifestParser.Parse("application/vnd.oci.image.manifest.v1+json", Json(ManifestJson()));

        Assert.False(parsed.IsIndex);
        Assert.Equal(Digest, parsed.Config!.Digest);
        Assert.Single(parsed.Layers);
        Assert.Equal(20, parsed.Layers[0].Size);
        Assert.Equal(2, parsed.BlobDigests.Count());
        Assert.Equal("application/vnd.oci.image.config.v1+json", parsed.ArtifactType);
        Assert.Null(parsed.Subject);
    }

    [Fact]
    public void ASubjectAndAnExplicitArtifactTypeAreRead()
    {
        var extra = $",\"artifactType\":\"application/vnd.example.sbom\",\"subject\":{{\"mediaType\":\"application/vnd.oci.image.manifest.v1+json\",\"digest\":\"{Digest}\",\"size\":5}},\"annotations\":{{\"a\":\"b\"}}";
        var parsed = OciManifestParser.Parse("application/vnd.oci.image.manifest.v1+json", Json(ManifestJson(extra: extra)));

        Assert.Equal("application/vnd.example.sbom", parsed.ArtifactType);
        Assert.Equal(Digest, parsed.Subject!.Digest);
        Assert.Equal("b", parsed.Annotations["a"]);
    }

    [Fact]
    public void AnIndexListsItsPlatforms()
    {
        var json = "{\"schemaVersion\":2,\"mediaType\":\"application/vnd.oci.image.index.v1+json\",\"manifests\":[" +
                   $"{{\"mediaType\":\"application/vnd.oci.image.manifest.v1+json\",\"digest\":\"{Digest}\",\"size\":7,\"platform\":{{\"os\":\"linux\",\"architecture\":\"arm64\",\"variant\":\"v8\"}}}}," +
                   "{\"mediaType\":\"application/vnd.oci.image.manifest.v1+json\",\"digest\":\"sha256:2222222222222222222222222222222222222222222222222222222222222222\",\"size\":8}]}";
        var parsed = OciManifestParser.Parse("application/vnd.oci.image.index.v1+json", Json(json));

        Assert.True(parsed.IsIndex);
        Assert.Equal(2, parsed.Children.Count);
        Assert.Equal(("linux", "arm64", "v8"), (parsed.Children[0].Os, parsed.Children[0].Architecture, parsed.Children[0].Variant));
        Assert.Null(parsed.Children[1].Os);
        Assert.Empty(parsed.BlobDigests);
    }

    public static IEnumerable<object[]> InvalidManifests()
    {
        yield return new object[] { "application/vnd.oci.image.manifest.v1+json", "not json" };
        yield return new object[] { "application/vnd.oci.image.manifest.v1+json", "[]" };
        yield return new object[] { "application/vnd.oci.image.manifest.v1+json", ManifestJson().Replace("\"schemaVersion\":2", "\"schemaVersion\":1") };
        yield return new object[] { "application/vnd.oci.image.manifest.v1+json", ManifestJson().Replace("\"mediaType\":\"application/vnd.oci.image.manifest.v1+json\"", "\"mediaType\":\"application/vnd.oci.image.index.v1+json\"") };
        yield return new object[] { "application/vnd.oci.image.manifest.v1+json", "{\"schemaVersion\":2,\"layers\":[]}" };
        yield return new object[] { "application/vnd.oci.image.manifest.v1+json", ManifestJson(config: "nodigest") };
        yield return new object[] { "application/vnd.oci.image.manifest.v1+json", ManifestJson().Replace("\"size\":10", "\"size\":-1") };
        yield return new object[] { "application/vnd.oci.image.index.v1+json", "{\"schemaVersion\":2}" };
        yield return new object[] { "text/plain", ManifestJson() };
        yield return new object[] { "application/vnd.docker.distribution.manifest.v1+json", ManifestJson() };
    }

    [Theory]
    [MemberData(nameof(InvalidManifests))]
    public void AManifestThatBreaksTheRulesIsRefused(string mediaType, string json)
    {
        var e = Assert.Throws<OciException>(() => OciManifestParser.Parse(mediaType, Json(json)));
        Assert.Equal(OciErrorCode.ManifestInvalid, e.Code);
    }

    [Fact]
    public void ADigestAlgorithmOtherThanSha256IsRefusedAsADigestError()
    {
        var e = Assert.Throws<OciException>(() => OciManifestParser.Parse("application/vnd.oci.image.manifest.v1+json", Json(ManifestJson(config: "sha512:abcdef"))));
        Assert.Equal(OciErrorCode.DigestInvalid, e.Code);
    }

    [Fact]
    public void ABomOrInvalidUtf8IsRefusedBecauseTheDigestMustSurviveStorage()
    {
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Json(ManifestJson())).ToArray();
        Assert.Equal(OciErrorCode.ManifestInvalid, Assert.Throws<OciException>(() => OciManifestParser.Parse("application/vnd.oci.image.manifest.v1+json", withBom)).Code);

        var invalid = Json(ManifestJson()).Concat(new byte[] { 0xFF }).ToArray();
        Assert.Equal(OciErrorCode.ManifestInvalid, Assert.Throws<OciException>(() => OciManifestParser.Parse("application/vnd.oci.image.manifest.v1+json", invalid)).Code);
    }
}
