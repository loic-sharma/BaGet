using System;
using System.Linq;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace BaGet.Clients.Tests;

/// <summary>
/// A real Docker daemon in a privileged <c>docker:dind</c> container (B6 of the Artifactory-parity plan), configured to treat one
/// address as a plain-http (insecure) registry, which is how the tests reach the BaGet server on the host
/// (<c>host.docker.internal:{port}</c>). The real <c>docker</c> CLI runs inside the same container, against the daemon's unix
/// socket, so nothing but the registry traffic crosses the container boundary. The daemon needs no internet: the tests build
/// their images <c>FROM scratch</c>. Skips the calling test when there is no Docker daemon or it refuses privileged containers.
/// </summary>
public sealed class DockerDaemon : IAsyncDisposable
{
    /// <summary>Docker 27 with a daemon (the version the registry flow was probed with, B3).</summary>
    public const string Image = "docker:27-dind";

    private readonly IContainer _container;

    private DockerDaemon(IContainer container)
    {
        _container = container;
    }

    /// <summary>Starts the daemon and waits until <c>docker info</c> answers.</summary>
    /// <param name="insecureRegistry">The <c>host:port</c> of the registry the daemon may talk plain http to.</param>
    public static Task<DockerDaemon> StartAsync(string insecureRegistry) => StartAsync(new[] { insecureRegistry });

    /// <summary>Starts the daemon and waits until <c>docker info</c> answers.</summary>
    /// <param name="insecureRegistries">The <c>host:port</c> of every registry the daemon may talk plain http to.</param>
    public static async Task<DockerDaemon> StartAsync(string[] insecureRegistries)
    {
        IContainer? container = null;
        try
        {
            container = new ContainerBuilder(Image)
                .WithPrivileged(true)
                // No TLS: the CLI in the same container uses the unix socket.
                .WithEnvironment("DOCKER_TLS_CERTDIR", string.Empty)
                .WithCommand(insecureRegistries.SelectMany(r => new[] { "--insecure-registry", r }).ToArray())
                .WithExtraHost(ClientContainers.HostAlias, "host-gateway")
                .Build();
            await container.StartAsync().ConfigureAwait(false);

            for (var attempt = 0; attempt < 60; attempt++)
            {
                var info = await container.ExecAsync(new[] { "docker", "info" }).ConfigureAwait(false);
                if (info.ExitCode == 0) return new DockerDaemon(container);

                await Task.Delay(1000).ConfigureAwait(false);
            }

            var logs = await container.GetLogsAsync().ConfigureAwait(false);
            throw new InvalidOperationException("The Docker daemon in docker:dind did not start: " + logs.Stderr);
        }
        catch (Exception e) when (IsDockerUnavailable(e))
        {
            Assert.Skip($"docker:dind needs Docker with privileged containers, which is not available: {e.GetBaseException().Message}");
            throw;
        }
    }

    /// <summary>
    /// Runs <paramref name="script"/> with <c>sh -c</c> in a fresh working directory with its own Docker client configuration, so
    /// no login leaks from one test to the next; the daemon (and its images) is shared.
    /// </summary>
    public async Task<CliResult> RunAsync(string script)
    {
        var result = await _container
            .ExecAsync(new[] { "sh", "-c", "set -e; d=$(mktemp -d); export DOCKER_CONFIG=$d/config; mkdir -p $DOCKER_CONFIG $d/work; cd $d/work; " + Normalize(script) })
            .ConfigureAwait(false);

        return new CliResult(result.ExitCode ?? -1, result.Stdout, result.Stderr);
    }

    // The test sources may have CRLF line ends; a shell script must not.
    private static string Normalize(string script) => script.Replace("\r\n", "\n");

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync().ConfigureAwait(false);
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
