using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BaGet.Tests;

/// <summary>An OTLP/HTTP request the collector received.</summary>
public sealed record CollectorRequest(string Path, string? XTest, byte[] Body)
{
    /// <summary>The body as latin-1 text: protobuf strings (service.name, span names) are readable in it.</summary>
    public string Text => Encoding.Latin1.GetString(Body);
}

/// <summary>
/// A loopback OTLP/HTTP receiver that answers 200 to every POST and records it, so the real exporters can be exercised
/// (path, headers, resource) without Docker. Not a protobuf decoder: tests look for strings in the body.
/// </summary>
public sealed class FakeOtlpCollector : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<CollectorRequest> _requests = new();

    private FakeOtlpCollector(WebApplication app, string url)
    {
        _app = app;
        Url = url;
    }

    public string Url { get; }

    public CollectorRequest[] Requests => _requests.ToArray();

    public static async Task<FakeOtlpCollector> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        FakeOtlpCollector? collector = null;

        app.MapPost("/{**path}", async (HttpContext context) =>
        {
            using var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer);
            collector!._requests.Enqueue(new CollectorRequest(
                context.Request.Path.Value ?? "",
                context.Request.Headers["x-test"].FirstOrDefault(),
                buffer.ToArray()));
            context.Response.ContentType = "application/x-protobuf";
            return Results.Ok();
        });

        await app.StartAsync();
        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        collector = new FakeOtlpCollector(app, url.Replace("[::1]", "127.0.0.1"));
        return collector;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
