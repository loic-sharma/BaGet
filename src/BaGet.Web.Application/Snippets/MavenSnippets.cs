using System.Collections.Generic;
using System.Net;

namespace BaGet.Web.Application.Snippets;

/// <summary>
/// The configuration the Maven pages show: the <c>settings.xml</c> server entry (the credentials Maven sends after the repository
/// challenges it), the POM's <c>distributionManagement</c> and <c>repositories</c>, the dependency to copy, and Gradle's
/// equivalents. One definition, so the list page, the artifact page and the upload page never disagree. Values are XML-escaped.
/// </summary>
internal static class MavenSnippets
{
    private static string X(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>The <c>&lt;server&gt;</c> entry for <c>~/.m2/settings.xml</c>; its id must be the one the POM's repository uses.</summary>
    public static IReadOnlyList<string> SettingsXml(string repositoryId) => new[]
    {
        "<settings>",
        "  <servers>",
        "    <server>",
        "      <id>" + X(repositoryId) + "</id>",
        "      <username>your user name</username>",
        "      <password>your API key</password>",
        "    </server>",
        "  </servers>",
        "</settings>",
    };

    /// <summary>
    /// The <c>&lt;mirror&gt;</c> entry for <c>~/.m2/settings.xml</c> that sends every request for Maven Central through this
    /// repository (a remote repository that proxies it); its id is also the <c>&lt;server&gt;</c> id of the credentials.
    /// </summary>
    public static IReadOnlyList<string> Mirror(string repositoryId, string url) => new[]
    {
        "<settings>",
        "  <mirrors>",
        "    <mirror>",
        "      <id>" + X(repositoryId) + "</id>",
        "      <mirrorOf>central</mirrorOf>",
        "      <url>" + X(url) + "</url>",
        "    </mirror>",
        "  </mirrors>",
        "</settings>",
    };

    /// <summary>The POM's <c>distributionManagement</c>: where <c>mvn deploy</c> sends releases and snapshots.</summary>
    public static IReadOnlyList<string> DistributionManagement(string repositoryId, string url) => new[]
    {
        "<distributionManagement>",
        "  <repository>",
        "    <id>" + X(repositoryId) + "</id>",
        "    <url>" + X(url) + "</url>",
        "  </repository>",
        "  <snapshotRepository>",
        "    <id>" + X(repositoryId) + "</id>",
        "    <url>" + X(url) + "</url>",
        "  </snapshotRepository>",
        "</distributionManagement>",
    };

    /// <summary>The POM's <c>repositories</c> entry a consumer adds to resolve from this repository.</summary>
    public static IReadOnlyList<string> Repositories(string repositoryId, string url, bool releases, bool snapshots) => new[]
    {
        "<repositories>",
        "  <repository>",
        "    <id>" + X(repositoryId) + "</id>",
        "    <url>" + X(url) + "</url>",
        "    <releases><enabled>" + (releases ? "true" : "false") + "</enabled></releases>",
        "    <snapshots><enabled>" + (snapshots ? "true" : "false") + "</enabled></snapshots>",
        "  </repository>",
        "</repositories>",
    };

    /// <summary>The dependency element for an artifact version.</summary>
    public static IReadOnlyList<string> Dependency(string groupId, string artifactId, string version) => new[]
    {
        "<dependency>",
        "  <groupId>" + X(groupId) + "</groupId>",
        "  <artifactId>" + X(artifactId) + "</artifactId>",
        "  <version>" + X(version) + "</version>",
        "</dependency>",
    };

    /// <summary>Gradle's repository block (Kotlin DSL) with the credentials from properties.</summary>
    public static IReadOnlyList<string> GradleRepository(string url) => new[]
    {
        "repositories {",
        "    maven {",
        "        url = uri(\"" + url + "\")",
        "        credentials {",
        "            username = providers.gradleProperty(\"mavenUser\").get()",
        "            password = providers.gradleProperty(\"mavenKey\").get()",
        "        }",
        "    }",
        "}",
    };

    /// <summary>Gradle's dependency notation for an artifact version.</summary>
    public static string GradleDependency(string groupId, string artifactId, string version)
        => $"implementation(\"{groupId}:{artifactId}:{version}\")";

    /// <summary>The command that deploys the project to the repository set up in the POM.</summary>
    public static string DeployCommand => "mvn deploy";

    /// <summary>The command that fetches an artifact by coordinates, for a quick check.</summary>
    public static string GetCommand(string groupId, string artifactId, string version, string repositoryId, string url)
        => $"mvn dependency:get -Dartifact={groupId}:{artifactId}:{version} -DremoteRepositories={repositoryId}::::{url}";
}
