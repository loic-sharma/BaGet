using System;
using System.Collections.Generic;
using System.Linq;
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
/// The real Docker CLI (27, in a privileged <c>docker:27-dind</c>) against the real BaGet application on a real socket
/// (P5 of the Artifactory-parity plan): <c>docker login</c> (the token flow, B3), <c>docker push</c> and <c>docker pull</c> of
/// images with several layers and of a large layer, a multi-platform image, a public and a private repository, deletion; and the
/// <c>opencontainers/distribution-spec</c> conformance suite (push, pull, content discovery, content management) run against the
/// same server. The images are built <c>FROM scratch</c>, so the daemon needs no internet. The tests skip without a Docker daemon
/// that allows privileged containers.
/// </summary>
public class DockerCliTests
{
    private const string User = "alice";

    /// <summary>The server and the daemon are started once and shared; every test uses repositories of its own.</summary>
    private sealed class Rig
    {
        public Rig(BaGetServer server, DockerDaemon daemon, string key)
        {
            Server = server;
            Daemon = daemon;
            Key = key;
        }

        public BaGetServer Server { get; }

        public DockerDaemon Daemon { get; }

        public string Key { get; }

        public string Host => Server.ContainerAuthority;
    }

    private static readonly Lazy<Task<Rig>> Shared = new(StartRigAsync);

    private static async Task<Rig> StartRigAsync()
    {
        var server = await BaGetServer.StartAsync(new Dictionary<string, string> { ["Accounts:Enabled"] = "true" });
        await server.CreateUserAsync(User);
        var key = await server.CreateKeyAsync(User);
        var daemon = await DockerDaemon.StartAsync(server.ContainerAuthority);

        return new Rig(server, daemon, key);
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

    private static string Login(Rig rig) => $"echo '{rig.Key}' | docker login {rig.Host} -u {User} --password-stdin\n";

    // An image of /layers/ layers of /size/ random bytes each, built FROM scratch (no base image to pull).
    private static string Build(string reference, int layers, int size, string platform = "linux/amd64")
        => $"""
            printf 'FROM scratch\n' > Dockerfile
            for i in $(seq 1 {layers}); do head -c {size} /dev/urandom > f$i; echo "COPY f$i /f$i" >> Dockerfile; done
            docker build -q --platform {platform} -t {reference} .

            """;

    private static string Field(CliResult result, string name)
    {
        var match = Regex.Match(result.Stdout, $@"^{name}=(.+)$", RegexOptions.Multiline);
        Assert.True(match.Success, $"No {name} in the output:\n{result.All}");

        return match.Groups[1].Value.Trim();
    }

    private static async Task<OciManifestContent?> ManifestAsync(Rig rig, string repository, string image, string reference)
    {
        using var scope = rig.Server.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>()
            .Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(repository, CancellationToken.None))!);

        try
        {
            return await scope.ServiceProvider.GetRequiredService<IOciRegistry>().GetManifestAsync(image, reference, CancellationToken.None);
        }
        catch (OciException e) when (e.Code == OciErrorCode.NameUnknown)
        {
            return null;
        }
    }

    [Fact]
    public async Task ALoggedInUserPushesAndPullsAMultiLayerImageByteForByte()
    {
        var rig = await RigAsync();
        var repository = Name();
        await rig.Server.CreateDockerRepositoryAsync(repository, anonymousRead: false);
        await rig.Server.GrantReadAsync(User, repository);
        var reference = $"{rig.Host}/{repository}/team/app:1.0";

        var result = await rig.Daemon.RunAsync(
            Login(rig)
            + Build(reference, layers: 3, size: 600_000)
            + $$$"""
               id=$(docker image inspect -f '{{.Id}}' {{{reference}}})
               layers=$(docker image inspect -f '{{json .RootFS.Layers}}' {{{reference}}})
               docker push {{{reference}}}
               docker rmi {{{reference}}}
               docker pull {{{reference}}}
               test "$(docker image inspect -f '{{.Id}}' {{{reference}}})" = "$id"
               test "$(docker image inspect -f '{{json .RootFS.Layers}}' {{{reference}}})" = "$layers"
               echo ID=$id
               echo DIGEST=$(docker image inspect -f '{{index .RepoDigests 0}}' {{{reference}}})

               """);

        Assert.True(result.ExitCode == 0, result.All);
        var digest = Field(result, "DIGEST").Split('@')[1];
        var stored = await ManifestAsync(rig, repository, "team/app", "1.0");
        Assert.Equal(digest, stored!.Digest);
        Assert.Equal(Field(result, "ID"), "sha256:" + Field(result, "ID").Substring(7));
    }

    [Fact]
    public async Task APrivateRepositoryRefusesAPullWithoutALoginAndAWrongPasswordFailsTheLogin()
    {
        var rig = await RigAsync();
        var repository = Name();
        await rig.Server.CreateDockerRepositoryAsync(repository, anonymousRead: false);
        await rig.Server.GrantReadAsync(User, repository);
        var reference = $"{rig.Host}/{repository}/app:1";

        var push = await rig.Daemon.RunAsync(Login(rig) + Build(reference, 1, 10_000) + $"docker push {reference}\n");
        Assert.True(push.ExitCode == 0, push.All);

        var anonymous = await rig.Daemon.RunAsync($"docker rmi {reference}; docker pull {reference}");
        Assert.NotEqual(0, anonymous.ExitCode);
        Assert.Matches("(?i)unauthorized|denied|authentication required", anonymous.All);

        var wrong = await rig.Daemon.RunAsync($"echo 'not-the-key' | docker login {rig.Host} -u {User} --password-stdin");
        Assert.NotEqual(0, wrong.ExitCode);
        Assert.DoesNotContain("Login Succeeded", wrong.All);

        var pulled = await rig.Daemon.RunAsync(Login(rig) + $"docker pull {reference}\n");
        Assert.True(pulled.ExitCode == 0, pulled.All);
    }

    [Fact]
    public async Task APublicRepositoryIsPulledWithoutLoggingIn()
    {
        var rig = await RigAsync();
        var repository = Name();
        await rig.Server.CreateDockerRepositoryAsync(repository, anonymousRead: true);
        var reference = $"{rig.Host}/{repository}/open:1";
        var push = await rig.Daemon.RunAsync(Login(rig) + Build(reference, 1, 10_000) + $"docker push {reference}\n");
        Assert.True(push.ExitCode == 0, push.All);

        var pull = await rig.Daemon.RunAsync($"docker rmi {reference}; docker pull {reference}");
        Assert.True(pull.ExitCode == 0, pull.All);

        // Anonymous callers may pull, never push.
        var anonymousPush = await rig.Daemon.RunAsync($"docker push {reference}");
        Assert.NotEqual(0, anonymousPush.ExitCode);
        Assert.Matches("(?i)unauthorized|denied|authentication required", anonymousPush.All);
    }

    [Fact]
    public async Task AUserWithoutAccessToTheRepositoryCannotPush()
    {
        var rig = await RigAsync();
        var repository = Name();
        await rig.Server.CreateDockerRepositoryAsync(repository, anonymousRead: true);
        // A key limited to another repository holds no action here.
        var other = Name();
        await rig.Server.CreateDockerRepositoryAsync(other);
        var limited = await rig.Server.CreateKeyAsync(User, other);
        var reference = $"{rig.Host}/{repository}/app:1";

        var result = await rig.Daemon.RunAsync(
            $"echo '{limited}' | docker login {rig.Host} -u {User} --password-stdin\n" + Build(reference, 1, 1000) + $"docker push {reference}\n");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Matches("(?i)unauthorized|denied|authentication required", result.All);
        Assert.Null(await ManifestAsync(rig, repository, "app", "1"));
    }

    [Fact]
    public async Task ALargeLayerIsPushedAndPulledIntact()
    {
        var rig = await RigAsync();
        var repository = Name();
        await rig.Server.CreateDockerRepositoryAsync(repository, anonymousRead: false);
        await rig.Server.GrantReadAsync(User, repository);
        var reference = $"{rig.Host}/{repository}/big:1";

        var result = await rig.Daemon.RunAsync(
            Login(rig)
            + Build(reference, layers: 1, size: 96_000_000)
            + $$$"""
               layers=$(docker image inspect -f '{{json .RootFS.Layers}}' {{{reference}}})
               docker push {{{reference}}}
               docker rmi {{{reference}}}
               docker pull {{{reference}}}
               test "$(docker image inspect -f '{{json .RootFS.Layers}}' {{{reference}}})" = "$layers"
               echo OK=1

               """);

        Assert.True(result.ExitCode == 0, result.All);
        Assert.Equal("1", Field(result, "OK"));
    }

    [Fact]
    public async Task AMultiPlatformImageIsPushedAsAnIndexAndPulledPerPlatform()
    {
        var rig = await RigAsync();
        var repository = Name();
        await rig.Server.CreateDockerRepositoryAsync(repository, anonymousRead: false);
        await rig.Server.GrantReadAsync(User, repository);
        var amd64 = $"{rig.Host}/{repository}/multi:amd64";
        var arm64 = $"{rig.Host}/{repository}/multi:arm64";
        var multi = $"{rig.Host}/{repository}/multi:latest";

        var result = await rig.Daemon.RunAsync(
            Login(rig)
            + Build(amd64, 1, 20_000, "linux/amd64")
            + $"rm -f f*; {Build(arm64, 1, 20_000, "linux/arm64").Replace("printf 'FROM scratch\\n' > Dockerfile\n", "printf 'FROM scratch\\n' > Dockerfile\n")}"
            + $$$"""
               docker push {{{amd64}}}
               docker push {{{arm64}}}
               docker manifest create --insecure {{{multi}}} {{{amd64}}} {{{arm64}}}
               docker manifest push --insecure {{{multi}}}
               docker rmi {{{amd64}}} {{{arm64}}}
               docker pull --platform linux/arm64 {{{multi}}}
               echo ARCH=$(docker image inspect -f '{{.Architecture}}' {{{multi}}})
               docker manifest inspect --insecure {{{multi}}} > index.json
               echo PLATFORMS=$(grep -c '"architecture"' index.json)

               """);

        Assert.True(result.ExitCode == 0, result.All);
        Assert.Equal("arm64", Field(result, "ARCH"));
        Assert.Equal("2", Field(result, "PLATFORMS"));

        var index = await ManifestAsync(rig, repository, "multi", "latest");
        Assert.True(OciNames.IsIndex(index!.MediaType), index.MediaType);

        using var scope = rig.Server.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>()
            .Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(repository, CancellationToken.None))!);
        var detail = await scope.ServiceProvider.GetRequiredService<IOciRegistry>().GetManifestDetailAsync("multi", index.Digest, CancellationToken.None);
        Assert.Equal(new[] { "linux/amd64", "linux/arm64" }, detail!.Platforms.Select(p => p.Platform).OrderBy(p => p).ToArray());
        Assert.All(detail.Platforms, p => Assert.True(p.Present));
    }

    [Fact]
    public async Task ADeletedManifestCannotBePulledAnyMore()
    {
        var rig = await RigAsync();
        var repository = Name();
        await rig.Server.CreateDockerRepositoryAsync(repository, anonymousRead: false);
        await rig.Server.GrantReadAsync(User, repository);
        var reference = $"{rig.Host}/{repository}/gone:1";
        var push = await rig.Daemon.RunAsync(Login(rig) + Build(reference, 1, 5_000) + $"docker push {reference}\n");
        Assert.True(push.ExitCode == 0, push.All);

        using (var scope = rig.Server.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IRepositoryContext>()
                .Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(repository, CancellationToken.None))!);
            var registry = scope.ServiceProvider.GetRequiredService<IOciRegistry>();
            var manifest = await registry.GetManifestAsync("gone", "1", CancellationToken.None);
            Assert.True(await registry.DeleteManifestAsync("gone", manifest!.Digest, CancellationToken.None));
        }

        var pull = await rig.Daemon.RunAsync(Login(rig) + $"docker rmi {reference}; docker pull {reference}");
        Assert.NotEqual(0, pull.ExitCode);
        Assert.Matches("(?i)not found|manifest unknown|no such", pull.All);
    }

    // ---- the opencontainers/distribution-spec conformance suite ----

    private const string ConformanceImage = "ghcr.io/opencontainers/distribution-spec/conformance:v1.1.0";

    [Fact]
    public async Task TheDistributionSpecConformanceSuitePasses()
    {
        var rig = await RigAsync();
        var repository = Name();
        await rig.Server.CreateDockerRepositoryAsync(repository, anonymousRead: false);
        await rig.Server.GrantReadAsync(User, repository);

        IContainer container;
        try
        {
            container = new ContainerBuilder(ConformanceImage)
                .WithEnvironment("OCI_ROOT_URL", $"http://{rig.Host}")
                .WithEnvironment("OCI_NAMESPACE", $"{repository}/myorg/myrepo")
                .WithEnvironment("OCI_CROSSMOUNT_NAMESPACE", $"{repository}/myorg/other")
                .WithEnvironment("OCI_USERNAME", User)
                .WithEnvironment("OCI_PASSWORD", rig.Key)
                .WithEnvironment("OCI_TEST_PUSH", "1")
                .WithEnvironment("OCI_TEST_PULL", "1")
                .WithEnvironment("OCI_TEST_CONTENT_DISCOVERY", "1")
                .WithEnvironment("OCI_TEST_CONTENT_MANAGEMENT", "1")
                .WithEnvironment("OCI_HIDE_SKIPPED_WORKFLOWS", "0")
                .WithEnvironment("OCI_DELETE_MANIFEST_BEFORE_BLOBS", "0")
                .WithExtraHost(ClientContainers.HostAlias, "host-gateway")
                .Build();
            await container.StartAsync(TestContext.Current.CancellationToken);
        }
        catch (Exception e) when (e.GetType().FullName!.Contains("Docker", StringComparison.OrdinalIgnoreCase) || e.GetBaseException().GetType().FullName!.Contains("Docker", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip($"The conformance image could not be started: {e.GetBaseException().Message}");
            throw;
        }

        try
        {
            // The suite runs to completion and exits; Ginkgo's summary (SUCCESS! or FAIL!) says how it ended.
            var deadline = DateTime.UtcNow.AddMinutes(10);
            var output = string.Empty;
            while (DateTime.UtcNow < deadline)
            {
                var logs = await container.GetLogsAsync(ct: TestContext.Current.CancellationToken);
                output = logs.Stdout + logs.Stderr;
                if (output.Contains("SUCCESS!") || output.Contains("FAIL!")) break;

                await Task.Delay(1000, TestContext.Current.CancellationToken);
            }

            var plain = Regex.Replace(output, "\u001b\\[[0-9;]*m", string.Empty);
            Assert.True(plain.Contains("SUCCESS!") && Regex.IsMatch(plain, @"\b0 Failed"), "The conformance suite failed:\n" + plain);
            Assert.Matches(@"Ran \d+ of \d+ Specs", plain);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }
}
