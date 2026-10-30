using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BaGet.Clients.Tests;

/// <summary>One request a CLI sent to a <see cref="HostRegistry"/>.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Target">Path and query exactly as sent (a scoped npm name is <c>/@scope%2fname</c>).</param>
/// <param name="Headers">The request headers, names lower-cased.</param>
/// <param name="Body">The body as text.</param>
public sealed record Recorded(string Method, string Target, IReadOnlyDictionary<string, string> Headers, string Body)
{
    /// <summary>The <c>Authorization</c> header, or null.</summary>
    public string? Authorization => Headers.TryGetValue("authorization", out var value) ? value : null;
}

/// <summary>What a <see cref="HostRegistry"/> answers.</summary>
/// <param name="Status">The HTTP status.</param>
/// <param name="Body">The body text.</param>
/// <param name="ContentType">Its media type.</param>
public sealed record HttpResponseData(int Status, string Body = "{}", string ContentType = "application/json");

/// <summary>
/// A real HTTP server on the host, listening on every interface so a container can reach it as
/// <c>host.docker.internal:{port}</c> (see <see cref="ClientContainers"/>). It records every request and answers through a
/// delegate; this is how the rig observes what a real CLI sends, and (in later phases) the place where the BaGet host under test
/// listens, since <c>WebApplicationFactory</c> has no socket a container could connect to.
/// </summary>
public sealed class HostRegistry : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<Recorded> _requests;

    private HostRegistry(WebApplication app, int port, ConcurrentQueue<Recorded> requests)
    {
        _app = app;
        Port = port;
        _requests = requests;
    }

    /// <summary>The TCP port.</summary>
    public int Port { get; }

    /// <summary>The address the container uses: <c>host.docker.internal:{port}</c>.</summary>
    public string ContainerAuthority => $"{ClientContainers.HostAlias}:{Port}";

    /// <summary>The registry URL the container uses, with a trailing slash.</summary>
    public string ContainerUrl => $"http://{ContainerAuthority}/";

    /// <summary>Every request so far, in arrival order.</summary>
    public IReadOnlyList<Recorded> Requests => _requests.ToArray();

    /// <summary>Starts a server that answers every request with <paramref name="handler"/>.</summary>
    public static async Task<HostRegistry> StartAsync(Func<Recorded, HttpResponseData> handler)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://0.0.0.0:0");
        var app = builder.Build();
        var queue = new ConcurrentQueue<Recorded>();

        app.Run(async context =>
        {
            var headers = context.Request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString());
            string body;
            using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8)) body = await reader.ReadToEndAsync();

            // Path is decoded; the raw target keeps a scoped npm name's %2f.
            var target = context.Features.Get<IHttpRequestFeature>()!.RawTarget;
            var recorded = new Recorded(context.Request.Method, target, headers, body);
            queue.Enqueue(recorded);

            var response = handler(recorded);
            context.Response.StatusCode = response.Status;
            context.Response.ContentType = response.ContentType;
            await context.Response.WriteAsync(response.Body);
        });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        return new HostRegistry(app, new Uri(address).Port, queue);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
