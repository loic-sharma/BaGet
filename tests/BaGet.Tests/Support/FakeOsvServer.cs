using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BaGet.Tests;

/// <summary>
/// A loopback stand-in for api.osv.dev: <c>POST /v1/querybatch</c> answers the advisory ids registered for a package and version,
/// <c>GET /v1/vulns/{id}</c> the record. No internet needed; counts what it was asked, and can be made to fail.
/// </summary>
public sealed class FakeOsvServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, JsonObject> _records = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Ecosystem, string Name, string Version), List<string>> _matches = new();
    private int _batches;
    private int _lookups;
    private int _queriedPackages;

    private FakeOsvServer(WebApplication app, string url)
    {
        _app = app;
        Url = url;
    }

    /// <summary>The address to configure as <c>Scanning:OsvUrl</c>.</summary>
    public string Url { get; }

    /// <summary>Whether every request is answered 503.</summary>
    public bool Down { get; set; }

    /// <summary>Calls of <c>/v1/querybatch</c>.</summary>
    public int Batches => Volatile.Read(ref _batches);

    /// <summary>Calls of <c>/v1/vulns/{id}</c>.</summary>
    public int Lookups => Volatile.Read(ref _lookups);

    /// <summary>Packages asked about, over all batches.</summary>
    public int QueriedPackages => Volatile.Read(ref _queriedPackages);

    /// <summary>Registers an advisory that affects exactly <paramref name="version"/> of the package.</summary>
    /// <param name="ecosystem">NuGet, npm or Maven.</param>
    /// <param name="name">The package.</param>
    /// <param name="version">The affected version.</param>
    /// <param name="id">The advisory id.</param>
    /// <param name="severity">GitHub's level name (LOW, MODERATE, HIGH, CRITICAL).</param>
    /// <param name="fixedIn">The first fixed version, or null.</param>
    public void Add(string ecosystem, string name, string version, string id, string severity = "HIGH", string? fixedIn = "99.0.0")
    {
        _records.GetOrAdd(id, _ => Record(ecosystem, name, id, severity, fixedIn));
        _matches.GetOrAdd((ecosystem, name, version), _ => new List<string>()).Add(id);
    }

    /// <summary>Forgets every advisory.</summary>
    public void Clear()
    {
        _records.Clear();
        _matches.Clear();
    }

    private static JsonObject Record(string ecosystem, string name, string id, string severity, string? fixedIn)
    {
        var events = new JsonArray(new JsonObject { ["introduced"] = "0" });
        if (fixedIn != null) events.Add(new JsonObject { ["fixed"] = fixedIn });

        return new JsonObject
        {
            ["id"] = id,
            ["summary"] = "Test advisory " + id,
            ["database_specific"] = new JsonObject { ["severity"] = severity },
            ["references"] = new JsonArray(new JsonObject { ["type"] = "ADVISORY", ["url"] = "https://example.test/advisories/" + id }),
            ["affected"] = new JsonArray(new JsonObject
            {
                ["package"] = new JsonObject { ["ecosystem"] = ecosystem, ["name"] = name },
                ["ranges"] = new JsonArray(new JsonObject { ["type"] = "ECOSYSTEM", ["events"] = events }),
            }),
        };
    }

    /// <summary>Starts a server on a free loopback port.</summary>
    public static async Task<FakeOsvServer> StartAsync()
    {
        // An empty content root: the appsettings.json next to the test binaries names Kestrel's endpoint (port 6060), which outranks
        // the address below and would make a second server fail to bind.
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = Directory.CreateTempSubdirectory("osv").FullName });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        FakeOsvServer? server = null;

        app.MapPost("/v1/querybatch", async (HttpContext context) =>
        {
            if (server!.Down) return Results.StatusCode(503);

            Interlocked.Increment(ref server._batches);
            using var document = await JsonDocument.ParseAsync(context.Request.Body);
            var results = new JsonArray();
            foreach (var query in document.RootElement.GetProperty("queries").EnumerateArray())
            {
                Interlocked.Increment(ref server._queriedPackages);
                var package = query.GetProperty("package");
                var key = (package.GetProperty("ecosystem").GetString()!, package.GetProperty("name").GetString()!, query.GetProperty("version").GetString()!);
                var vulns = new JsonArray();
                if (server._matches.TryGetValue(key, out var ids))
                {
                    foreach (var id in ids) vulns.Add(new JsonObject { ["id"] = id, ["modified"] = "2024-01-01T00:00:00Z" });
                }

                results.Add(vulns.Count == 0 ? new JsonObject() : new JsonObject { ["vulns"] = vulns });
            }

            return Results.Text(new JsonObject { ["results"] = results }.ToJsonString(), "application/json");
        });

        app.MapGet("/v1/vulns/{id}", (string id) =>
        {
            if (server!.Down) return Results.StatusCode(503);

            Interlocked.Increment(ref server._lookups);
            return server._records.TryGetValue(id, out var record)
                ? Results.Text(record.ToJsonString(), "application/json")
                : Results.NotFound();
        });

        await app.StartAsync();
        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        server = new FakeOsvServer(app, url.Replace("[::1]", "127.0.0.1"));
        return server;
    }

    /// <summary>The record <see cref="Add"/> would serve, for a test that writes an offline copy.</summary>
    public JsonObject RecordOf(string id) => (JsonObject)_records[id].DeepClone();

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
