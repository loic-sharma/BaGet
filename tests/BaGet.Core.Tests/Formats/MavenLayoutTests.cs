using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace BaGet.Core.Tests.Formats;

/// <summary>The Maven layout rules: paths, file names, version order, generated metadata, POM facts and repository settings.</summary>
public class MavenLayoutTests
{
    // ---- paths ----

    [Fact]
    public void AFilePathIsSplitIntoGroupArtifactVersionAndFile()
    {
        var path = MavenNames.ParsePath("org/apache/maven/maven-core/3.9.6/maven-core-3.9.6.jar")!;

        Assert.Equal(MavenPathKind.File, path.Kind);
        var coordinate = Assert.Single(path.Candidates);
        Assert.Equal(new MavenCoordinate("org.apache.maven", "maven-core", "3.9.6"), coordinate);
        Assert.Equal("org.apache.maven:maven-core", coordinate.Name);
        Assert.Equal("maven-core-3.9.6.jar", path.BaseName);
        Assert.Null(path.ChecksumAlgorithm);
    }

    [Theory]
    [InlineData("sha1")]
    [InlineData("md5")]
    [InlineData("sha256")]
    [InlineData("sha512")]
    public void AChecksumPathNamesTheFileItIsAChecksumOf(string algorithm)
    {
        var path = MavenNames.ParsePath($"g/a/1.0/a-1.0.jar.{algorithm}")!;

        Assert.Equal(algorithm, path.ChecksumAlgorithm);
        Assert.Equal("a-1.0.jar", path.BaseName);
    }

    [Fact]
    public void MetadataPathsHaveTheVersionAndTheArtifactReading()
    {
        var path = MavenNames.ParsePath("org/acme/lib/1.0-SNAPSHOT/maven-metadata.xml")!;

        Assert.Equal(MavenPathKind.Metadata, path.Kind);
        Assert.Equal(
            new[] { new MavenCoordinate("org.acme", "lib", "1.0-SNAPSHOT"), new MavenCoordinate("org.acme.lib", "1.0-SNAPSHOT", null) },
            path.Candidates);
        Assert.Null(path.GroupOnly);
    }

    [Fact]
    public void ArtifactLevelMetadataOfAShortPathHasOnlyThatReading()
    {
        var path = MavenNames.ParsePath("acme/lib/maven-metadata.xml.sha1")!;

        Assert.Equal(MavenPathKind.Metadata, path.Kind);
        Assert.Equal("sha1", path.ChecksumAlgorithm);
        Assert.Equal(new MavenCoordinate("acme", "lib", null), Assert.Single(path.Candidates));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a.jar")]
    [InlineData("g/a/1.0")]
    [InlineData("g//a/1.0/a-1.0.jar")]
    [InlineData("g/../a/1.0/a-1.0.jar")]
    [InlineData("g/a/../a-1.0.jar")]
    [InlineData("g/a/1.0/a 1.0.jar")]
    [InlineData("g/a/1.0/a-1.0.jar\\x")]
    [InlineData("g.h/a/1.0/a-1.0.jar")]
    [InlineData("g/a/1.0/%2e%2e")]
    [InlineData("g/a/1.0/a-1.0.jar\n")]
    [InlineData("g/a\n/1.0/a-1.0.jar")]
    [InlineData("g/a/1.0\n/a-1.0.jar")]
    public void APathThatIsNotTheLayoutIsRefused(string? raw)
    {
        Assert.Null(MavenNames.ParsePath(raw));
    }

    [Fact]
    public void AGroupFolderWithADotIsNotAGroupFolder()
    {
        // "a/b" and "a.b" would be the same group in the database; the layout has folders only.
        Assert.Null(MavenNames.ParsePath("a.b/lib/1.0/lib-1.0.jar"));
    }

    // ---- file names ----

    [Theory]
    [InlineData("lib-1.0.jar", null, "jar")]
    [InlineData("lib-1.0.pom", null, "pom")]
    [InlineData("lib-1.0-sources.jar", "sources", "jar")]
    [InlineData("lib-1.0-javadoc.jar", "javadoc", "jar")]
    [InlineData("lib-1.0.tar.gz", null, "tar.gz")]
    [InlineData("lib-1.0.jar.asc", null, "jar.asc")]
    [InlineData("lib-1.0-linux-x86_64.zip", "linux-x86_64", "zip")]
    public void AReleaseFileNameIsArtifactVersionClassifierExtension(string name, string? classifier, string extension)
    {
        var parts = MavenNames.ParseFileName("lib", "1.0", name)!;

        Assert.Equal(classifier, parts.Classifier);
        Assert.Equal(extension, parts.Extension);
        Assert.Null(parts.SnapshotTimestamp);
    }

    [Theory]
    [InlineData("other-1.0.jar")]
    [InlineData("lib-2.0.jar")]
    [InlineData("lib-1.0")]
    [InlineData("lib-1.0.")]
    [InlineData("lib-1.0.1.jar")]
    [InlineData("lib-1.0-.jar")]
    [InlineData("lib-1.0x.jar")]
    [InlineData("lib-1.0.jar\n")]
    [InlineData("lib-1.0-sources\n.jar")]
    public void AFileNameOfAnotherArtifactOrVersionIsRefused(string name)
    {
        Assert.Null(MavenNames.ParseFileName("lib", "1.0", name));
    }

    [Fact]
    public void ATimestampedSnapshotBuildCarriesItsTimestampAndNumber()
    {
        var parts = MavenNames.ParseFileName("lib", "1.0-SNAPSHOT", "lib-1.0-20240131.235959-12-tests.jar")!;

        Assert.Equal("20240131.235959", parts.SnapshotTimestamp);
        Assert.Equal(12, parts.SnapshotBuild);
        Assert.Equal("tests", parts.Classifier);
        Assert.Equal("jar", parts.Extension);
    }

    [Fact]
    public void ALiteralSnapshotFileNameIsAcceptedWithoutATimestamp()
    {
        var parts = MavenNames.ParseFileName("lib", "1.0-SNAPSHOT", "lib-1.0-SNAPSHOT.jar")!;

        Assert.Null(parts.SnapshotTimestamp);
        Assert.Equal(0, parts.SnapshotBuild);
    }

    [Theory]
    [InlineData("lib-1.0-20241331.235959-1.jar")]
    [InlineData("lib-1.0-20240131.235959-0.jar")]
    [InlineData("lib-1.0-2024-1.jar")]
    public void ASnapshotBuildWithAnImpossibleTimestampOrNumberIsRefused(string name)
    {
        Assert.Null(MavenNames.ParseFileName("lib", "1.0-SNAPSHOT", name));
    }

    // ---- names ----

    [Theory]
    [InlineData("org.apache.maven", true)]
    [InlineData("com.google.guava", true)]
    [InlineData("a", true)]
    [InlineData("a_b-c.d", true)]
    [InlineData("", false)]
    [InlineData("a..b", false)]
    [InlineData(".a", false)]
    [InlineData("a.", false)]
    [InlineData("a/b", false)]
    [InlineData("a:b", false)]
    public void GroupIdsAreDotSeparatedFolderNames(string groupId, bool valid)
    {
        Assert.Equal(valid, MavenNames.IsValidGroupId(groupId));
    }

    [Fact]
    public void AnArtifactNameSplitsAtTheColon()
    {
        Assert.True(MavenNames.TryParseArtifactName("org.acme:lib", out var group, out var artifact));
        Assert.Equal("org.acme", group);
        Assert.Equal("lib", artifact);
        Assert.False(MavenNames.TryParseArtifactName("lib", out _, out _));
        Assert.False(MavenNames.TryParseArtifactName(":lib", out _, out _));
        Assert.False(MavenNames.TryParseArtifactName("org:", out _, out _));
    }

    // ---- version order ----

    [Fact]
    public void VersionsAreOrderedTheWayMavenOrdersThem()
    {
        var ordered = new[] { "1.0", "1.0.1", "1.0-rc1", "1.0-beta2", "1.0-alpha", "1.0-SNAPSHOT", "1.10", "1.2", "1.0-sp1", "2.0-SNAPSHOT" };

        var sorted = ordered.OrderBy(v => v, Comparer<string>.Create(MavenNames.CompareVersions)).ToList();

        Assert.Equal(
            new[] { "1.0-alpha", "1.0-beta2", "1.0-rc1", "1.0-SNAPSHOT", "1.0", "1.0-sp1", "1.0.1", "1.2", "1.10", "2.0-SNAPSHOT" },
            sorted);
    }

    // ---- metadata ----

    [Fact]
    public void ArtifactMetadataListsVersionsAndNamesLatestAndRelease()
    {
        var xml = XDocument.Parse(MavenMetadata.ForArtifact("org.acme", "lib", new[] { "1.0", "2.0-SNAPSHOT", "1.1" }, new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc)));

        var versioning = xml.Root!.Element("versioning")!;
        Assert.Equal("org.acme", xml.Root.Element("groupId")!.Value);
        Assert.Equal("lib", xml.Root.Element("artifactId")!.Value);
        Assert.Equal("2.0-SNAPSHOT", versioning.Element("latest")!.Value);
        Assert.Equal("1.1", versioning.Element("release")!.Value);
        Assert.Equal(new[] { "1.0", "1.1", "2.0-SNAPSHOT" }, versioning.Element("versions")!.Elements("version").Select(v => v.Value));
        Assert.Equal("20240304050607", versioning.Element("lastUpdated")!.Value);
    }

    [Fact]
    public void SnapshotMetadataNamesTheNewestBuildAndOneVersionPerClassifierAndExtension()
    {
        var files = new[]
        {
            new MavenFileInfo("lib-1.0-20240101.100000-1.jar", null, "jar", "20240101.100000", 1, new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc)),
            new MavenFileInfo("lib-1.0-20240101.100000-1.pom", null, "pom", "20240101.100000", 1, new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc)),
            new MavenFileInfo("lib-1.0-20240102.110000-2.jar", null, "jar", "20240102.110000", 2, new DateTime(2024, 1, 2, 11, 0, 0, DateTimeKind.Utc)),
            new MavenFileInfo("lib-1.0-20240102.110000-2-sources.jar", "sources", "jar", "20240102.110000", 2, new DateTime(2024, 1, 2, 11, 0, 0, DateTimeKind.Utc)),
        };

        var xml = XDocument.Parse(MavenMetadata.ForVersion("org.acme", "lib", "1.0-SNAPSHOT", files, new DateTime(2024, 1, 2, 11, 0, 0, DateTimeKind.Utc)));

        var versioning = xml.Root!.Element("versioning")!;
        Assert.Equal("1.0-SNAPSHOT", xml.Root.Element("version")!.Value);
        Assert.Equal("20240102.110000", versioning.Element("snapshot")!.Element("timestamp")!.Value);
        Assert.Equal("2", versioning.Element("snapshot")!.Element("buildNumber")!.Value);

        var values = versioning.Element("snapshotVersions")!.Elements("snapshotVersion")
            .Select(s => (Classifier: s.Element("classifier")?.Value, Extension: s.Element("extension")!.Value, Value: s.Element("value")!.Value))
            .ToList();
        Assert.Equal(3, values.Count);
        Assert.Contains((null, "jar", "1.0-20240102.110000-2"), values);
        Assert.Contains((null, "pom", "1.0-20240101.100000-1"), values);
        Assert.Contains(("sources", "jar", "1.0-20240102.110000-2"), values);
    }

    [Fact]
    public void ReleaseVersionMetadataHasNoSnapshotSection()
    {
        var xml = XDocument.Parse(MavenMetadata.ForVersion("g", "a", "1.0", Array.Empty<MavenFileInfo>(), DateTime.UtcNow));

        Assert.Null(xml.Root!.Element("versioning"));
    }

    // ---- POM ----

    private const string Pom = """
        <?xml version="1.0" encoding="UTF-8"?>
        <project xmlns="http://maven.apache.org/POM/4.0.0">
          <modelVersion>4.0.0</modelVersion>
          <parent><groupId>org.acme</groupId><artifactId>parent</artifactId><version>3</version></parent>
          <artifactId>lib</artifactId>
          <packaging>bundle</packaging>
          <name>The   Library</name>
          <description>Does
            things.</description>
          <url>https://acme.example/lib</url>
          <dependencies>
            <dependency><groupId>junit</groupId><artifactId>junit</artifactId><version>4.13</version><scope>test</scope></dependency>
            <dependency><groupId>org.slf4j</groupId><artifactId>slf4j-api</artifactId><optional>true</optional></dependency>
          </dependencies>
        </project>
        """;

    [Fact]
    public void APomGivesItsFactsAndInheritsTheParentsGroupAndVersion()
    {
        var pom = MavenPom.TryParse(new MemoryStream(Encoding.UTF8.GetBytes(Pom)))!;

        Assert.Equal("org.acme", pom.GroupId);
        Assert.Equal("lib", pom.ArtifactId);
        Assert.Equal("3", pom.Version);
        Assert.Equal("bundle", pom.Packaging);
        Assert.Equal("The Library", pom.Name);
        Assert.Equal("Does things.", pom.Description);
        Assert.Equal("org.acme:parent:3", pom.Parent);
        Assert.Equal(2, pom.Dependencies.Count);
        Assert.Equal(new MavenPomDependency("junit", "junit", "4.13", "test", false), pom.Dependencies[0]);
        Assert.True(pom.Dependencies[1].Optional);
    }

    [Fact]
    public void APomWithADoctypeIsNotReadAndAnEntityIsNeverExpanded()
    {
        const string hostile = "<?xml version=\"1.0\"?><!DOCTYPE project [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><project><name>&x;</name></project>";

        Assert.Null(MavenPom.TryParse(new MemoryStream(Encoding.UTF8.GetBytes(hostile))));
    }

    [Fact]
    public void BytesThatAreNotAPomAreNotRead()
    {
        Assert.Null(MavenPom.TryParse(new MemoryStream(new byte[] { 1, 2, 3 })));
        Assert.Null(MavenPom.TryParse(new MemoryStream(Encoding.UTF8.GetBytes("<other/>"))));
    }

    [Fact]
    public void AGeneratedPomReadsBackAsTheCoordinatesItWasMadeFrom()
    {
        var text = MavenPom.Generate("org.acme", "lib", "1.2", "jar", "Lib", "A <lib> & more");

        var pom = MavenPom.TryParse(new MemoryStream(Encoding.UTF8.GetBytes(text)))!;

        Assert.Equal("org.acme", pom.GroupId);
        Assert.Equal("lib", pom.ArtifactId);
        Assert.Equal("1.2", pom.Version);
        Assert.Equal("A <lib> & more", pom.Description);
    }

    // ---- settings ----

    [Fact]
    public void SettingsDefaultToImmutableReleases()
    {
        var settings = MavenRepositorySettings.Parse(null);

        Assert.Equal(MavenPolicy.Releases, settings.Policy);
        Assert.False(settings.AllowRedeploy);
        Assert.Equal(0, settings.MaxSnapshotBuilds);
        Assert.True(settings.Accepts("1.0"));
        Assert.False(settings.Accepts("1.0-SNAPSHOT"));
    }

    [Fact]
    public void SettingsRoundTripThroughTheirJson()
    {
        var json = new MavenRepositorySettings { Policy = MavenPolicy.Both, AllowRedeploy = true, MaxSnapshotBuilds = 5 }.ToJson();

        var back = MavenRepositorySettings.Parse(json);

        Assert.Equal(MavenPolicy.Both, back.Policy);
        Assert.True(back.AllowRedeploy);
        Assert.Equal(5, back.MaxSnapshotBuilds);
        Assert.True(back.Accepts("1.0-SNAPSHOT"));
        Assert.True(back.Accepts("1.0"));
    }

    [Fact]
    public void SettingsFromTheFormTakeNamesAndRefuseTheRest()
    {
        var settings = MavenRepositorySettings.FromFields(new Dictionary<string, string?> { ["policy"] = "snapshots", ["redeploy"] = "Yes", ["snapshotBuilds"] = "3" });

        Assert.Equal(MavenPolicy.Snapshots, settings.Policy);
        Assert.True(settings.AllowRedeploy);
        Assert.Equal(3, settings.MaxSnapshotBuilds);
        Assert.True(settings.Accepts("2.0-SNAPSHOT"));
        Assert.False(settings.Accepts("2.0"));

        Assert.Throws<ArgumentException>(() => MavenRepositorySettings.FromFields(new Dictionary<string, string?> { ["policy"] = "all" }));
        Assert.Throws<ArgumentException>(() => MavenRepositorySettings.FromFields(new Dictionary<string, string?> { ["redeploy"] = "maybe" }));
        Assert.Throws<ArgumentException>(() => MavenRepositorySettings.FromFields(new Dictionary<string, string?> { ["snapshotBuilds"] = "-1" }));
        Assert.Throws<ArgumentException>(() => MavenRepositorySettings.FromFields(new Dictionary<string, string?> { ["snapshotBuilds"] = "100000" }));
    }

    [Fact]
    public void UnreadableSettingsFallBackToTheDefaults()
    {
        Assert.Equal(MavenPolicy.Releases, MavenRepositorySettings.Parse("{not json").Policy);
    }
}
