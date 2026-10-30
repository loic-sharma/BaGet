using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace BaGet.Tests;

/// <summary>An npm tarball with the checksums a client sends for it.</summary>
internal sealed record NpmTarballFile(string Name, string Version, byte[] Bytes, string Integrity, string Shasum);

/// <summary>Builds npm tarballs and the <c>PUT /{name}</c> bodies <c>npm publish</c> sends, so the registry can be driven over HTTP without the npm client.</summary>
internal static class NpmTestPackages
{
    /// <summary>A gzip-compressed tar with <c>package/package.json</c>, <c>package/index.js</c> and a readme.</summary>
    public static NpmTarballFile Tarball(
        string name,
        string version,
        string description = "A test package",
        string readme = "# Readme\n\nHello.",
        IDictionary<string, string>? dependencies = null,
        string? packageJsonName = null,
        string? packageJsonVersion = null)
    {
        var json = new JsonObject
        {
            ["name"] = packageJsonName ?? name,
            ["version"] = packageJsonVersion ?? version,
            ["description"] = description,
            ["license"] = "MIT",
        };

        if (dependencies != null)
        {
            var map = new JsonObject();
            foreach (var pair in dependencies) map[pair.Key] = pair.Value;
            json["dependencies"] = map;
        }

        var raw = new MemoryStream();
        using (var gzip = new GZipStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            Add(tar, "package/package.json", json.ToJsonString());
            Add(tar, "package/index.js", "module.exports = 1;\n");
            Add(tar, "package/README.md", readme);
        }

        var bytes = raw.ToArray();

        return new NpmTarballFile(name, version, bytes, "sha512-" + Convert.ToBase64String(SHA512.HashData(bytes)), Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant());
    }

    private static void Add(TarWriter tar, string path, string content)
    {
        var entry = new PaxTarEntry(TarEntryType.RegularFile, path) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) };
        tar.WriteEntry(entry);
    }

    /// <summary>The JSON body <c>npm publish</c> sends: the version, its dist (with a placeholder tarball URL) and the tarball as base64.</summary>
    public static string PublishBody(NpmTarballFile tarball, string tag = "latest", string? readme = "# Readme", string? integrity = null, string? shasum = null, IDictionary<string, string>? dependencies = null)
    {
        var fileName = tarball.Name.Contains('/') ? tarball.Name + "-" + tarball.Version + ".tgz" : tarball.Name + "-" + tarball.Version + ".tgz";
        var version = new JsonObject
        {
            ["name"] = tarball.Name,
            ["version"] = tarball.Version,
            ["description"] = "A test package",
            ["_id"] = tarball.Name + "@" + tarball.Version,
            ["dist"] = new JsonObject
            {
                ["integrity"] = integrity ?? tarball.Integrity,
                ["shasum"] = shasum ?? tarball.Shasum,
                ["tarball"] = "http://localhost:1/" + tarball.Name + "/-/" + fileName,
            },
        };

        if (dependencies != null)
        {
            var map = new JsonObject();
            foreach (var pair in dependencies) map[pair.Key] = pair.Value;
            version["dependencies"] = map;
        }

        var root = new JsonObject
        {
            ["_id"] = tarball.Name,
            ["name"] = tarball.Name,
            ["description"] = "A test package",
            ["dist-tags"] = new JsonObject { [tag] = tarball.Version },
            ["versions"] = new JsonObject { [tarball.Version] = version },
            ["access"] = null,
            ["_attachments"] = new JsonObject
            {
                [fileName] = new JsonObject
                {
                    ["content_type"] = "application/octet-stream",
                    ["data"] = Convert.ToBase64String(tarball.Bytes),
                    ["length"] = tarball.Bytes.Length,
                },
            },
        };
        if (readme != null) root["readme"] = readme;

        return root.ToJsonString();
    }

    /// <summary>The request path of a package below the registry <paramref name="registry"/> (<c>/r/web/npm/</c>); a scoped name is encoded as npm does.</summary>
    public static string Path(string registry, string name) => registry + name.Replace("/", "%2f");

    /// <summary>A JSON request with an optional bearer token.</summary>
    public static HttpRequestMessage Json(HttpMethod method, string uri, string? body = null, string? token = null)
    {
        var request = new HttpRequestMessage(method, uri);
        if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return request;
    }
}
