using System.Collections.Generic;

namespace BaGet.Web.Application.Snippets;

/// <summary>
/// The commands and configuration the Docker pages show: <c>docker login</c> with an API key, pull and push by tag or digest, and
/// the daemon setting a plain-http registry needs. Image references are <c>{host}/{repository}/{image}</c>: the BaGet repository
/// is the first path segment.
/// </summary>
public static class DockerSnippets
{
    /// <summary>The login command; the password is an API key.</summary>
    public static string Login(string host) => $"docker login {host}";

    /// <summary>The reference of an image in the registry, with a tag or a digest.</summary>
    public static string Reference(string host, string repository, string image, string? tag = null, string? digest = null)
        => $"{host}/{repository}/{image}" + (digest != null ? "@" + digest : tag != null ? ":" + tag : string.Empty);

    /// <summary>The pull command.</summary>
    public static string Pull(string host, string repository, string image, string? tag = null, string? digest = null)
        => "docker pull " + Reference(host, repository, image, tag, digest);

    /// <summary>The commands that push a local image to the registry.</summary>
    public static IReadOnlyList<string> Push(string host, string repository, string image, string tag = "latest")
        => new[]
        {
            $"docker tag my-image:{tag} {Reference(host, repository, image, tag)}",
            $"docker push {Reference(host, repository, image, tag)}",
        };

    /// <summary>A multi-platform push with buildx.</summary>
    public static string BuildxPush(string host, string repository, string image, string tag = "latest")
        => $"docker buildx build --platform linux/amd64,linux/arm64 -t {Reference(host, repository, image, tag)} --push .";

    /// <summary>The daemon setting that allows a registry served over plain http (<c>/etc/docker/daemon.json</c>).</summary>
    public static IReadOnlyList<string> InsecureRegistry(string host)
        => new[]
        {
            "{",
            $"  \"insecure-registries\": [\"{host}\"]",
            "}",
        };

    /// <summary>The curl call that gets a token for a scope with an API key, for scripts.</summary>
    public static string Token(string tokenUrl, string repository, string image, string actions = "pull")
        => $"curl -u user:API_KEY \"{tokenUrl}?service=BaGet&scope=repository:{repository}/{image}:{actions}\"";
}
