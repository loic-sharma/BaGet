using System;
using System.Collections.Generic;

namespace BaGet.Web.Application.Snippets;

/// <summary>
/// The commands and <c>.npmrc</c> lines the npm pages show: how to point a client at the repository's registry, sign in, install
/// and publish. One definition, so the list page, the package page and the upload page never disagree.
/// </summary>
internal static class NpmInstallSnippets
{
    /// <summary>The <c>.npmrc</c> key under which npm keeps the token for a registry: <c>//host:port/path/:_authToken</c>.</summary>
    public static string AuthKey(string registryUrl)
    {
        var uri = new Uri(registryUrl);

        return "//" + uri.Authority + uri.AbsolutePath.TrimEnd('/') + "/:_authToken";
    }

    /// <summary>
    /// The registry line of an <c>.npmrc</c>: <c>registry=…</c> for everything, or <c>@scope:registry=…</c> when the package is
    /// scoped (so packages of other scopes keep coming from npmjs).
    /// </summary>
    public static string RegistryLine(string registryUrl, string? scope)
        => (scope == null ? "registry" : scope + ":registry") + "=" + registryUrl;

    /// <summary>The install tabs for <paramref name="name"/>: the install command, and the client configuration it needs.</summary>
    public static IReadOnlyList<InstallSnippet> Build(string name, string? version, string? tag, string registryUrl)
    {
        var scope = NpmInfoName.Scope(name);
        var spec = version == null ? name : name + "@" + version;

        var install = new List<string> { "npm install " + spec };
        if (tag != null && tag != "latest") install.Add("npm install " + name + "@" + tag);

        return new[]
        {
            new InstallSnippet { Name = "npm", Prefix = ">", Lines = install },
            new InstallSnippet { Name = "yarn", Prefix = ">", Lines = new[] { "yarn add " + spec } },
            new InstallSnippet { Name = "pnpm", Prefix = ">", Lines = new[] { "pnpm add " + spec } },
            new InstallSnippet
            {
                Name = ".npmrc",
                Lines = new[]
                {
                    RegistryLine(registryUrl, scope),
                    AuthKey(registryUrl) + "=<your API key or the token npm login stored>",
                },
            },
        };
    }

    /// <summary>The commands that sign in and publish to the registry.</summary>
    public static IReadOnlyList<string> PublishLines(string registryUrl)
        => new[]
        {
            "npm login --registry=" + registryUrl,
            "npm publish --registry=" + registryUrl,
        };
}
