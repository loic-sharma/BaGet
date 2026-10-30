using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Web.V3.Extensions;

namespace BaGet.Clients.Tests;

/// <summary>
/// The real BaGet application (<c>Program.BuildApplication</c>, SQLite and a file store in a temporary folder) listening on a
/// real socket on every interface, so a client in a container reaches it as <c>host.docker.internal:{port}</c>.
/// <c>WebApplicationFactory</c> has no socket, which is why the CLI tests cannot use it. Tarball and registry addresses are built
/// from the <c>Host</c> header the client sent, so they point back at this server.
/// </summary>
public sealed class BaGetServer : IAsyncDisposable
{
    /// <summary>The password of the users <see cref="CreateUserAsync"/> makes.</summary>
    public const string Password = "Passw0rd!";

    private readonly WebApplication _app;
    private readonly string _directory;

    private BaGetServer(WebApplication app, int port, string directory)
    {
        _app = app;
        Port = port;
        _directory = directory;
    }

    /// <summary>The TCP port.</summary>
    public int Port { get; }

    /// <summary>The application's services.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary><c>host.docker.internal:{port}</c>, as the container reaches the server.</summary>
    public string ContainerAuthority => $"{ClientContainers.HostAlias}:{Port}";

    /// <summary>The npm registry of repository <paramref name="repository"/> as the container addresses it (with a trailing slash).</summary>
    public string RegistryUrl(string repository) => $"http://{ContainerAuthority}/r/{repository}/npm/";

    /// <summary>The <c>.npmrc</c> key of the token for <paramref name="repository"/>'s registry.</summary>
    public string AuthKey(string repository) => $"//{ContainerAuthority}/r/{repository}/npm/:_authToken";

    /// <summary>Starts the application with <paramref name="settings"/> on top of a temporary SQLite database and file store.</summary>
    public static async Task<BaGetServer> StartAsync(IDictionary<string, string>? settings = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "BaGetClientTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var all = new Dictionary<string, string>
        {
            ["Database:Type"] = "Sqlite",
            ["Database:ConnectionString"] = "Data Source=" + Path.Combine(directory, "BaGet.db"),
            ["Storage:Type"] = "FileSystem",
            ["Storage:Path"] = Path.Combine(directory, "Packages"),
            ["Search:Type"] = "Database",
            [SettingsOverridesStore.PathKey] = Path.Combine(directory, SettingsOverridesStore.DefaultFileName),
            ["Accounts:Enabled"] = "false",
            ["Accounts:CreateDefaultAdministrator"] = "false",
            ["Serilog:WriteTo:Console:Enabled"] = "false",
            ["Serilog:WriteTo:File:Enabled"] = "false",
            ["Serilog:EventLog:Enabled"] = "false",
            ["Kestrel:Endpoints:Http:Url"] = "http://0.0.0.0:0",
            // The shipped appsettings.json exports traces to localhost:4317; a test server must not.
            ["Telemetry:Enabled"] = "false",
        };
        foreach (var pair in settings ?? new Dictionary<string, string>()) all[pair.Key] = pair.Value;

        var app = Program.BuildApplication(all.Select(p => $"--{p.Key}={p.Value}").ToArray());
        await app.RunMigrationsAsync(CancellationToken.None);
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        return new BaGetServer(app, new Uri(address.Replace("[::]", "localhost").Replace("0.0.0.0", "localhost")).Port, directory);
    }

    /// <summary>Creates a local npm repository.</summary>
    public async Task CreateNpmRepositoryAsync(string name, bool anonymousRead = true)
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(name, RepositoryFormat.Npm, RepositoryKind.Local, new Dictionary<string, string?>(), anonymousRead, CancellationToken.None);
    }

    /// <summary>The address of this server as the test process itself reaches it (the process hosts it, so <c>localhost</c>).</summary>
    public string LocalRegistryUrl(string repository) => $"http://localhost:{Port}/r/{repository}/npm/";

    /// <summary>The Maven repository <paramref name="repository"/> as the container addresses it (no trailing slash).</summary>
    public string MavenUrl(string repository) => $"http://{ContainerAuthority}/r/{repository}/maven";

    /// <summary>The Maven repository as the test process itself reaches it (no trailing slash).</summary>
    public string LocalMavenUrl(string repository) => $"http://localhost:{Port}/r/{repository}/maven";

    /// <summary>Creates a local Maven repository (<paramref name="policy"/>: Releases, Snapshots or Both).</summary>
    public async Task CreateMavenRepositoryAsync(string name, string policy = "Releases", bool redeploy = false, bool anonymousRead = true)
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>().CreateAsync(
            name,
            RepositoryFormat.Maven,
            RepositoryKind.Local,
            new Dictionary<string, string?> { ["policy"] = policy, ["redeploy"] = redeploy ? "Yes" : "No", ["snapshotBuilds"] = "0" },
            anonymousRead,
            CancellationToken.None);
    }

    /// <summary>Creates a local Docker repository (its registry is at <c>/v2/{name}/</c>).</summary>
    public async Task CreateDockerRepositoryAsync(string name, bool anonymousRead = true)
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(name, RepositoryFormat.Docker, RepositoryKind.Local, new Dictionary<string, string?>(), anonymousRead, CancellationToken.None);
    }

    /// <summary>Creates a remote Docker repository that caches the registry at <paramref name="upstreamUrl"/>.</summary>
    public async Task CreateRemoteDockerRepositoryAsync(string name, string upstreamUrl, Action<RemoteRepositorySettings>? change = null)
    {
        var settings = new RemoteRepositorySettings { Url = upstreamUrl, Protocol = RemoteProtocol.Docker };
        change?.Invoke(settings);

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(name, RepositoryFormat.Docker, RepositoryKind.Remote, settings, anonymousRead: true, CancellationToken.None);
    }

    /// <summary>Creates a virtual Docker repository of <paramref name="members"/> (in order) that deploys to <paramref name="deploy"/>.</summary>
    public async Task CreateVirtualDockerRepositoryAsync(string name, string[] members, string? deploy)
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().CreateAsync(
            name,
            new VirtualRepositorySettings { Members = members.ToList(), DeployRepository = deploy },
            CancellationToken.None,
            actor: null,
            anonymousRead: true,
            RepositoryFormat.Docker);
    }

    /// <summary>Switches a remote repository's offline mode.</summary>
    public async Task SetRemoteOfflineAsync(string name, bool offline)
    {
        using var scope = Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>();
        var settings = (await admin.FindAsync(name, CancellationToken.None))!.Settings;
        settings.Offline = offline;
        await admin.UpdateAsync(name, settings, clearPassword: false, CancellationToken.None);
    }

    /// <summary>The bytes of layers a remote Docker repository keeps.</summary>
    public async Task<long> StoredDockerBytesAsync(string name)
    {
        using var scope = Services.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<IRemoteRepositoryAdministration>().GetCacheAsync(name, CancellationToken.None)).StoredBytes;
    }

    /// <summary>Creates a remote Maven repository that caches the repository at <paramref name="upstreamUrl"/>.</summary>
    public async Task CreateRemoteMavenRepositoryAsync(string name, string upstreamUrl, Action<RemoteRepositorySettings>? change = null)
    {
        var settings = new RemoteRepositorySettings { Url = upstreamUrl, Protocol = RemoteProtocol.Maven };
        change?.Invoke(settings);

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(name, RepositoryFormat.Maven, RepositoryKind.Remote, settings, anonymousRead: true, CancellationToken.None);
    }

    /// <summary>Creates a virtual Maven repository of <paramref name="members"/> (in order) that deploys to <paramref name="deploy"/>.</summary>
    public async Task CreateVirtualMavenRepositoryAsync(string name, string[] members, string? deploy)
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().CreateAsync(
            name,
            new VirtualRepositorySettings { Members = members.ToList(), DeployRepository = deploy },
            CancellationToken.None,
            actor: null,
            anonymousRead: true,
            RepositoryFormat.Maven);
    }

    /// <summary>The artifacts (<c>group:artifact</c>) with their versions that the repository itself stored (rows), whatever its upstream says.</summary>
    public async Task<IReadOnlyDictionary<string, string[]>> StoredMavenAsync(string repository)
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>()
            .Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(repository, CancellationToken.None))!);

        var maven = scope.ServiceProvider.GetRequiredService<MavenRepositoryService>();
        var result = new Dictionary<string, string[]>();
        foreach (var hit in (await maven.SearchAsync(null, 0, 250, null, CancellationToken.None)).Hits)
        {
            var artifact = await maven.GetArtifactAsync(hit.GroupId, hit.ArtifactId, CancellationToken.None);
            result[hit.CoordinateName] = artifact!.Versions.Select(v => v.Version).ToArray();
        }

        return result;
    }

    /// <summary>Creates a remote npm repository that caches the registry at <paramref name="upstreamUrl"/>.</summary>
    public async Task CreateRemoteNpmRepositoryAsync(string name, string upstreamUrl, Action<RemoteRepositorySettings>? change = null)
    {
        var settings = new RemoteRepositorySettings { Url = upstreamUrl, Protocol = RemoteProtocol.Npm };
        change?.Invoke(settings);

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
            .CreateAsync(name, RepositoryFormat.Npm, RepositoryKind.Remote, settings, anonymousRead: true, CancellationToken.None);
    }

    /// <summary>Creates a virtual npm repository of <paramref name="members"/> (in order) that deploys to <paramref name="deploy"/>.</summary>
    public async Task CreateVirtualNpmRepositoryAsync(string name, string[] members, string? deploy)
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>().CreateAsync(
            name,
            new VirtualRepositorySettings { Members = members.ToList(), DeployRepository = deploy },
            CancellationToken.None,
            actor: null,
            anonymousRead: true,
            RepositoryFormat.Npm);
    }

    /// <summary>What the repository itself stored of a package (rows), whatever its upstream says; null when nothing.</summary>
    public async Task<NpmPackageInfo?> StoredAsync(string repository, string name)
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>()
            .Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(repository, CancellationToken.None))!);

        return await scope.ServiceProvider.GetRequiredService<NpmRegistryService>().GetPackageAsync(name, CancellationToken.None);
    }

    /// <summary>Creates a user with <see cref="Password"/>.</summary>
    public async Task CreateUserAsync(string name, bool administrator = false)
    {
        using var scope = Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>().CreateAsync(
            new BaGetUser { UserName = name, IsAdministrator = administrator, Created = DateTime.UtcNow },
            Password);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    /// <summary>Lets <paramref name="user"/> read everything in <paramref name="repository"/> (a private repository has no readers until someone is granted).</summary>
    public async Task GrantReadAsync(string user, string repository)
    {
        using var scope = Services.CreateScope();
        var account = await scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>().FindByNameAsync(user) ?? throw new InvalidOperationException("No such user.");
        var access = scope.ServiceProvider.GetRequiredService<IAccessAdministration>();
        var target = $"{user} reads {repository}";
        await access.CreateTargetAsync(target, new[] { repository }, null, null, null, null, CancellationToken.None);
        await access.SetGrantAsync(target, PrincipalType.User, account.Id, AccessAction.Read, null, CancellationToken.None);
    }

    /// <summary>Makes an API key for a user (all scopes), optionally limited to repositories.</summary>
    public async Task<string> CreateKeyAsync(string user, params string[] repositories)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BaGetUser>>();
        var account = await users.FindByNameAsync(user) ?? throw new InvalidOperationException("No such user.");
        var service = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
        var keys = new List<int>();
        foreach (var repository in repositories) keys.Add((await service.FindAsync(repository, CancellationToken.None))!.Key);

        return await scope.ServiceProvider.GetRequiredService<ApiKeyService>()
            .CreateAsync(account.Id, "test", ApiKeyScopes.All, "*", null, keys, CancellationToken.None);
    }

    /// <summary>The package, or null, as the registry holds it.</summary>
    public async Task<NpmPackageInfo?> PackageAsync(string repository, string name)
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRepositoryContext>()
            .Use((await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(repository, CancellationToken.None))!);

        return await scope.ServiceProvider.GetRequiredService<INpmRegistry>().GetPackageAsync(name, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
