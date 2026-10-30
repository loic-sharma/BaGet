using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Clients.Tests;

/// <summary>
/// The real Docker CLI (27, in a privileged <c>docker:27-dind</c>) against remote and virtual Docker repositories (P5). The upstream of a
/// remote repository is a registry the tests run themselves, not the internet: a second real BaGet application (a private repository
/// that wants the <c>docker login</c> token flow with an API key, and a public one), and the reference <c>registry:2</c> from the
/// Docker project (an open registry). The proxy under test is a third BaGet, reached by the daemon as <c>host.docker.internal</c>; it
/// reaches its upstreams as <c>localhost</c>. Images are built <c>FROM scratch</c>. All skip without a Docker daemon that allows
/// privileged containers.
/// </summary>
public class DockerRemoteVirtualCliTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";

    private sealed class Rig
    {
        public Rig(BaGetServer upstream, BaGetServer proxy, DockerDaemon daemon, string aliceKey, string bobKey, IContainer? registry, int registryPort)
        {
            Upstream = upstream;
            Proxy = proxy;
            Daemon = daemon;
            AliceKey = aliceKey;
            BobKey = bobKey;
            Registry = registry;
            RegistryPort = registryPort;
        }

        /// <summary>The registry a remote repository caches (a real BaGet, accounts on).</summary>
        public BaGetServer Upstream { get; }

        /// <summary>The server under test.</summary>
        public BaGetServer Proxy { get; }

        public DockerDaemon Daemon { get; }

        public string AliceKey { get; }

        public string BobKey { get; }

        /// <summary>The reference <c>registry:2</c>, or null when it could not be started.</summary>
        public IContainer? Registry { get; }

        public int RegistryPort { get; }

        public string UpstreamHost => Upstream.ContainerAuthority;

        public string ProxyHost => Proxy.ContainerAuthority;
    }

    private static readonly Lazy<Task<Rig>> Shared = new(StartRigAsync);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<Rig> StartRigAsync()
    {
        var upstream = await BaGetServer.StartAsync(new Dictionary<string, string> { ["Accounts:Enabled"] = "true" });
        await upstream.CreateUserAsync(Alice);
        var aliceKey = await upstream.CreateKeyAsync(Alice);

        var proxy = await BaGetServer.StartAsync(new Dictionary<string, string> { ["Accounts:Enabled"] = "true" });
        await proxy.CreateUserAsync(Bob);
        var bobKey = await proxy.CreateKeyAsync(Bob);

        // The reference registry, on a port chosen up front so the daemon can be told it is an insecure (plain http) one. It is optional:
        // the images come from Docker Hub the first time.
        IContainer? registry = null;
        var registryPort = FreePort();
        try
        {
            registry = new ContainerBuilder("registry:2")
                .WithPortBinding(registryPort, 5000)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(5000).ForPath("/v2/")))
                .Build();
            await registry.StartAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            registry = null;
        }

        var daemon = await DockerDaemon.StartAsync(new[]
        {
            upstream.ContainerAuthority,
            proxy.ContainerAuthority,
            $"{ClientContainers.HostAlias}:{registryPort}",
        });

        return new Rig(upstream, proxy, daemon, aliceKey, bobKey, registry, registryPort);
    }

    private static async Task<Rig> RigAsync()
    {
        try
        {
            return await Shared.Value.ConfigureAwait(false);
        }
        catch (Exception e) when (e is not Xunit.Sdk.XunitException && e.GetType().Name != "SkipException" && !e.GetType().FullName!.Contains("Xunit", StringComparison.Ordinal))
        {
            Assert.Skip($"The Docker rig could not start (it needs Docker with privileged containers): {e.GetBaseException().Message}");
            throw;
        }
    }

    private static string Name() => "t" + Guid.NewGuid().ToString("N").Substring(0, 8);

    private static string Login(string host, string user, string key) => $"echo '{key}' | docker login {host} -u {user} --password-stdin\n";

    // An image of /layers/ layers of /size/ random bytes each, built FROM scratch (no base image to pull).
    private static string Build(string reference, int layers, int size)
        => $"""
            printf 'FROM scratch\n' > Dockerfile
            for i in $(seq 1 {layers}); do head -c {size} /dev/urandom > f$i; echo "COPY f$i /f$i" >> Dockerfile; done
            docker build -q -t {reference} .

            """;

    private static string Field(CliResult result, string name)
    {
        var match = Regex.Match(result.Stdout, $@"^{name}=(.+)$", RegexOptions.Multiline);
        Assert.True(match.Success, $"No {name} in the output:\n{result.All}");

        return match.Groups[1].Value.Trim();
    }

    private static async Task<T> InRepoAsync<T>(BaGetServer server, string repository, Func<IServiceProvider, Task<T>> action)
    {
        using var scope = server.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>()
            .Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(repository, CancellationToken.None))!);

        return await action(scope.ServiceProvider);
    }

    private static Task<OciManifestContent?> ManifestAsync(BaGetServer server, string repository, string image, string reference)
        => InRepoAsync(server, repository, sp => sp.GetRequiredService<OciRegistryService>().FindManifestAsync(image, reference, CancellationToken.None));

    // Pushes an image of one repository of the upstream and says its id, config digest and layer digests.
    private static async Task<(string Id, string Digest, string Layer)> SeedAsync(Rig rig, string host, string key, string user, string reference)
    {
        var result = await rig.Daemon.RunAsync(
            (key.Length == 0 ? string.Empty : Login(host, user, key))
            + Build(reference, layers: 2, size: 300_000)
            + $$$"""
               docker push {{{reference}}}
               echo ID=$(docker image inspect -f '{{.Id}}' {{{reference}}})
               echo DIGEST=$(docker image inspect -f '{{index .RepoDigests 0}}' {{{reference}}})
               echo LAYER=$(docker image inspect -f '{{index .RootFS.Layers 0}}' {{{reference}}})

               """
            + Forget(reference));
        Assert.True(result.ExitCode == 0, result.All);

        return (Field(result, "ID"), Field(result, "DIGEST").Split('@')[1], Field(result, "LAYER"));
    }

    // Removes an image and everything the build left behind (layers kept by the build cache would make the next pull skip them: the daemon
    // does not download a layer it has), so the next pull downloads every layer.
    private static string Forget(string reference) => $"docker rmi {reference} || true; docker builder prune -af >/dev/null 2>&1; docker image prune -af >/dev/null 2>&1\n";

    private static string Pull(string reference)
        => $$$"""
              docker pull {{{reference}}}
              echo ID=$(docker image inspect -f '{{.Id}}' {{{reference}}})
              echo DIGEST=$(docker image inspect -f '{{index .RepoDigests 0}}' {{{reference}}})

              """;

    [Fact]
    public async Task APullThroughARemoteRepositoryFetchesFromAPrivateUpstreamThroughItsTokenFlowAndKeepsTheImage()
    {
        var rig = await RigAsync();
        var source = Name();
        await rig.Upstream.CreateDockerRepositoryAsync(source, anonymousRead: false);
        await rig.Upstream.GrantReadAsync(Alice, source);
        var seeded = await SeedAsync(rig, rig.UpstreamHost, rig.AliceKey, Alice, $"{rig.UpstreamHost}/{source}/team/app:1");

        var hub = Name();
        await rig.Proxy.CreateRemoteDockerRepositoryAsync(hub, $"http://localhost:{rig.Upstream.Port}", s =>
        {
            s.Username = Alice;
            s.Password = rig.AliceKey;
        });
        var reference = $"{rig.ProxyHost}/{hub}/{source}/team/app:1";

        // Anonymous to the proxy; the proxy logs in to the private upstream by itself.
        var result = await rig.Daemon.RunAsync(Pull(reference));

        Assert.True(result.ExitCode == 0, result.All);
        Assert.Equal(seeded.Id, Field(result, "ID"));
        Assert.Equal(seeded.Digest, Field(result, "DIGEST").Split('@')[1]);
        Assert.NotNull(await ManifestAsync(rig.Proxy, hub, $"{source}/team/app", "1"));

        // The layers were kept while they streamed: the second pull needs no upstream.
        var kept = await rig.Proxy.StoredDockerBytesAsync(hub);
        Assert.True(kept > 590_000, $"the layers were kept while they streamed (kept {kept} bytes)");
        await rig.Proxy.SetRemoteOfflineAsync(hub, offline: true);
        var again = await rig.Daemon.RunAsync(Forget(reference) + Pull(reference));
        Assert.True(again.ExitCode == 0, again.All);
        Assert.Equal(seeded.Id, Field(again, "ID"));
    }

    [Fact]
    public async Task APullThroughARemoteRepositoryWithAWrongKeyFailsAndKeepsNothing()
    {
        var rig = await RigAsync();
        var source = Name();
        await rig.Upstream.CreateDockerRepositoryAsync(source, anonymousRead: false);
        await rig.Upstream.GrantReadAsync(Alice, source);
        await SeedAsync(rig, rig.UpstreamHost, rig.AliceKey, Alice, $"{rig.UpstreamHost}/{source}/team/app:1");
        var hub = Name();
        await rig.Proxy.CreateRemoteDockerRepositoryAsync(hub, $"http://localhost:{rig.Upstream.Port}", s =>
        {
            s.Username = Alice;
            s.Password = "not-the-key";
        });

        var result = await rig.Daemon.RunAsync($"docker pull {rig.ProxyHost}/{hub}/{source}/team/app:1");

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("not-the-key", result.All);
        Assert.Null(await ManifestAsync(rig.Proxy, hub, $"{source}/team/app", "1"));
    }

    [Fact]
    public async Task APublicUpstreamNeedsNoCredentialsAndANewTagAppearsAfterTheMetadataPeriod()
    {
        var rig = await RigAsync();
        var source = Name();
        await rig.Upstream.CreateDockerRepositoryAsync(source, anonymousRead: true);
        var first = await SeedAsync(rig, rig.UpstreamHost, rig.AliceKey, Alice, $"{rig.UpstreamHost}/{source}/team/lib:1");
        var hub = Name();
        await rig.Proxy.CreateRemoteDockerRepositoryAsync(hub, $"http://localhost:{rig.Upstream.Port}", s => s.MetadataTtlSeconds = 0);
        var reference = $"{rig.ProxyHost}/{hub}/{source}/team/lib:1";

        var pulled = await rig.Daemon.RunAsync(Pull(reference));
        Assert.True(pulled.ExitCode == 0, pulled.All);
        Assert.Equal(first.Id, Field(pulled, "ID"));

        // The tag moves upstream; with no metadata period the proxy follows at once.
        var second = await SeedAsync(rig, rig.UpstreamHost, rig.AliceKey, Alice, $"{rig.UpstreamHost}/{source}/team/lib:1");
        var repulled = await rig.Daemon.RunAsync(Forget(reference) + Pull(reference));

        Assert.True(repulled.ExitCode == 0, repulled.All);
        Assert.Equal(second.Id, Field(repulled, "ID"));
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task APullThroughTheReferenceRegistryWorks()
    {
        var rig = await RigAsync();
        if (rig.Registry == null) Assert.Skip("The reference registry:2 image could not be started on this machine.");

        var seeded = await SeedAsync(rig, $"{ClientContainers.HostAlias}:{rig.RegistryPort}", string.Empty, "-", $"{ClientContainers.HostAlias}:{rig.RegistryPort}/team/lib:1");
        var hub = Name();
        await rig.Proxy.CreateRemoteDockerRepositoryAsync(hub, $"http://localhost:{rig.RegistryPort}");

        var result = await rig.Daemon.RunAsync(Pull($"{rig.ProxyHost}/{hub}/team/lib:1"));

        Assert.True(result.ExitCode == 0, result.All);
        Assert.Equal(seeded.Id, Field(result, "ID"));
        var kept = await rig.Proxy.StoredDockerBytesAsync(hub);
        Assert.True(kept > 590_000, $"the layers were kept while they streamed (kept {kept} bytes)");
        Assert.NotNull(await ManifestAsync(rig.Proxy, hub, "team/lib", "1"));
    }

    [Fact]
    public async Task AVirtualRepositoryPullsFromItsMembersAndPushesToItsDeployRepository()
    {
        var rig = await RigAsync();
        var source = Name();
        await rig.Upstream.CreateDockerRepositoryAsync(source, anonymousRead: true);
        var theirs = await SeedAsync(rig, rig.UpstreamHost, rig.AliceKey, Alice, $"{rig.UpstreamHost}/{source}/team/lib:1");

        var local = Name();
        var hub = Name();
        var all = Name();
        await rig.Proxy.CreateDockerRepositoryAsync(local, anonymousRead: true);
        await rig.Proxy.CreateRemoteDockerRepositoryAsync(hub, $"http://localhost:{rig.Upstream.Port}");
        await rig.Proxy.CreateVirtualDockerRepositoryAsync(all, new[] { local, hub }, local);

        var mine = $"{rig.ProxyHost}/{all}/mine/app:1";
        var viaVirtual = $"{rig.ProxyHost}/{all}/{source}/team/lib:1";
        var result = await rig.Daemon.RunAsync(
            Login(rig.ProxyHost, Bob, rig.BobKey)
            + Build(mine, layers: 1, size: 200_000)
            + $$$"""
               mineId=$(docker image inspect -f '{{.Id}}' {{{mine}}})
               docker push {{{mine}}}
               docker rmi {{{mine}}}; docker builder prune -af >/dev/null 2>&1; docker image prune -af >/dev/null 2>&1
               docker pull {{{mine}}}
               test "$(docker image inspect -f '{{.Id}}' {{{mine}}})" = "$mineId"
               echo MINE=$mineId

               """
            + Pull(viaVirtual));

        Assert.True(result.ExitCode == 0, result.All);
        Assert.Equal(theirs.Id, Field(result, "ID"));

        // The push landed in the deploy repository only; the pull of the upstream's image was cached by the remote member.
        Assert.NotNull(await ManifestAsync(rig.Proxy, local, "mine/app", "1"));
        Assert.Null(await ManifestAsync(rig.Proxy, hub, "mine/app", "1"));
        Assert.NotNull(await ManifestAsync(rig.Proxy, hub, $"{source}/team/lib", "1"));
        Assert.Null(await ManifestAsync(rig.Proxy, local, $"{source}/team/lib", "1"));
    }
}
