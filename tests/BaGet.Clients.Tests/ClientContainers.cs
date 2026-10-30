using System;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace BaGet.Clients.Tests;

/// <summary>What a command run in a client container printed and returned.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="Stdout">Standard output.</param>
/// <param name="Stderr">Standard error.</param>
public sealed record CliResult(long ExitCode, string Stdout, string Stderr)
{
    /// <summary>Both streams, for assertion messages.</summary>
    public string All => Stdout + Stderr;
}

/// <summary>
/// Real package-manager CLIs in containers (B6 of the Artifactory-parity plan): <c>node</c> with the real <c>npm</c> and <c>maven</c> with the
/// real <c>mvn</c>; <c>docker:dind</c> (privileged) follows with its phase. Each image starts once per run and is shared; a
/// test that needs one skips (<see cref="Assert.Skip(string)"/>) when there is no Docker daemon, so the normal suite stays green
/// without it, as in <c>BaGet.Database.Tests</c>.
/// </summary>
public static class ClientContainers
{
    /// <summary>The name containers use for the machine the tests run on.</summary>
    public const string HostAlias = "host.docker.internal";

    /// <summary>Node 22 LTS on Alpine; ships npm 10 (the version the recorded flows were captured with).</summary>
    public const string NodeImage = "node:22-alpine";

    /// <summary>Maven 3.9 on a Temurin 21 JDK (the real <c>mvn</c> and <c>jar</c>).</summary>
    public const string MavenImage = "maven:3.9-eclipse-temurin-21";

    private static readonly Lazy<Task<IContainer>> NodeStart = new(() => StartAsync(NodeImage));
    private static readonly Lazy<Task<IContainer>> MavenStart = new(() => StartAsync(MavenImage));

    /// <summary>The shared maven container (idle; commands are executed in it). Skips the calling test when Docker is unavailable.</summary>
    public static async Task<IContainer> MavenAsync()
    {
        try
        {
            return await MavenStart.Value.ConfigureAwait(false);
        }
        catch (Exception e) when (IsDockerUnavailable(e))
        {
            Assert.Skip($"The Maven client container needs Docker, which is not available: {e.GetBaseException().Message}");
            throw;
        }
    }

    /// <summary>The shared node container (idle; commands are executed in it). Skips the calling test when Docker is unavailable.</summary>
    public static async Task<IContainer> NodeAsync()
    {
        try
        {
            return await NodeStart.Value.ConfigureAwait(false);
        }
        catch (Exception e) when (IsDockerUnavailable(e))
        {
            Assert.Skip($"The npm client container needs Docker, which is not available: {e.GetBaseException().Message}");
            throw;
        }
    }

    /// <summary>
    /// Runs <paramref name="script"/> with <c>sh -c</c> in the container, in a fresh directory with an empty home, so each test
    /// starts with no npm configuration.
    /// </summary>
    public static async Task<CliResult> RunAsync(IContainer container, string script)
    {
        var result = await container
            .ExecAsync(new[] { "sh", "-c", "d=$(mktemp -d); export HOME=$d/home; mkdir -p $HOME $d/work; cd $d/work; " + script })
            .ConfigureAwait(false);

        return new CliResult(result.ExitCode ?? -1, result.Stdout, result.Stderr);
    }

    private static async Task<IContainer> StartAsync(string image)
    {
        var container = new ContainerBuilder(image)
            .WithEntrypoint("sleep")
            .WithCommand("infinity")
            // host-gateway makes host.docker.internal resolve on Linux hosts too (Docker Desktop defines it already).
            .WithExtraHost(HostAlias, "host-gateway")
            .Build();
        await container.StartAsync().ConfigureAwait(false);

        return container;
    }

    private static bool IsDockerUnavailable(Exception e)
    {
        for (var inner = e; inner != null; inner = inner.InnerException)
        {
            if ((inner.GetType().FullName ?? string.Empty).Contains("Docker", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
