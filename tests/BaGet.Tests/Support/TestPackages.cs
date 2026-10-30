using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using NuGet.Packaging;
using NuGet.Versioning;

namespace BaGet.Tests;

/// <summary>
/// Builds minimal packages in memory, for tests that need more than the one checked in.
/// </summary>
internal static class TestPackages
{
    /// <param name="dependencies">Dependencies declared for every framework, as (id, version range) pairs.</param>
    public static MemoryStream Build(string id, string version, string description = "Test package", params (string Id, string Range)[] dependencies)
    {
        var builder = new PackageBuilder
        {
            Id = id,
            Version = NuGetVersion.Parse(version),
            Description = description,
        };
        builder.Authors.Add("Test author");
        builder.Files.Add(new InMemoryFile("content/readme.txt", "test"));
        if (dependencies.Length > 0)
        {
            builder.DependencyGroups.Add(new PackageDependencyGroup(
                NuGet.Frameworks.NuGetFramework.AnyFramework,
                dependencies.Select(d => new NuGet.Packaging.Core.PackageDependency(d.Id, NuGet.Versioning.VersionRange.Parse(d.Range)))));
        }

        var stream = new MemoryStream();
        builder.Save(stream);
        stream.Position = 0;

        return stream;
    }

    /// <summary>
    /// A push request as "dotnet nuget push" sends it.
    /// </summary>
    public static HttpRequestMessage PushRequest(Stream package, string? apiKey)
    {
        var content = new MultipartFormDataContent();
        var file = new StreamContent(package);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "package", "package.nupkg");

        var request = new HttpRequestMessage(HttpMethod.Put, "api/v2/package") { Content = content };
        if (apiKey != null)
        {
            request.Headers.Add("X-NuGet-ApiKey", apiKey);
        }

        return request;
    }

    private class InMemoryFile : IPackageFile
    {
        private readonly byte[] _content;

        public InMemoryFile(string path, string content)
        {
            Path = path;
            _content = Encoding.UTF8.GetBytes(content);
        }

        public string Path { get; }
        public string EffectivePath => Path;
        public NuGet.Frameworks.NuGetFramework? NuGetFramework => null;
        public System.Runtime.Versioning.FrameworkName? TargetFramework => null;
        public System.DateTimeOffset LastWriteTime => System.DateTimeOffset.UnixEpoch;

        public Stream GetStream() => new MemoryStream(_content);
    }
}