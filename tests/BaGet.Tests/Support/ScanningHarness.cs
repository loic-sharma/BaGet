using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The application with vulnerability scanning on and a <see cref="FakeOsvServer"/> as its OSV: the setup of the scanning tests.
/// The scheduled rescan is off (<c>RescanInterval</c> zero), so only what a test does scans.
/// </summary>
public sealed class ScanningHarness : IAsyncDisposable
{
    public const string Key = "secret-key";

    private ScanningHarness(BaGetApplication app, FakeOsvServer osv, HttpClient client)
    {
        App = app;
        Osv = osv;
        Client = client;
    }

    public BaGetApplication App { get; }

    public FakeOsvServer Osv { get; }

    public HttpClient Client { get; }

    public static async Task<ScanningHarness> StartAsync(
        ITestOutputHelper output,
        Action<IServiceCollection>? configureServices = null,
        Dictionary<string, string>? settings = null)
    {
        var osv = await FakeOsvServer.StartAsync();
        var configuration = new Dictionary<string, string>
        {
            { "ApiKey", Key },
            { "Scanning:Enabled", "true" },
            { "Scanning:OsvUrl", osv.Url },
            { "Scanning:RescanInterval", "00:00:00" },
            // Only what a test does scans; the tests of the background scan switch this on.
            { "Scanning:ScanOnDeploy", "false" },
        };
        foreach (var pair in settings ?? new Dictionary<string, string>()) configuration[pair.Key] = pair.Value;

        var app = new BaGetApplication(output, configOverrides: configuration, configureServices: configureServices);
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        return new ScanningHarness(app, osv, client);
    }

    /// <summary>Runs <paramref name="action"/> in a scope whose repository is <paramref name="repository"/> (null: default).</summary>
    public async Task<T> RunAsync<T>(string? repository, Func<IServiceProvider, Task<T>> action)
    {
        using var scope = await App.CreateRepositoryScopeAsync(repository, TestContext.Current.CancellationToken);

        return await action(scope.ServiceProvider);
    }

    /// <summary>Scans one version now.</summary>
    public Task<ScanResult?> ScanAsync(string? repository, string name, string version)
        => RunAsync(repository, sp => sp.GetRequiredService<IScanService>().ScanAsync(name, version, TestContext.Current.CancellationToken));

    /// <summary>The stored result of a version with its findings.</summary>
    public Task<ScanResult?> ResultAsync(string? repository, string name, string version)
        => RunAsync(repository, sp => sp.GetRequiredService<IScanService>().FindAsync(name, version, TestContext.Current.CancellationToken));

    /// <summary>Saves the policy of a repository.</summary>
    public async Task SetPolicyAsync(string? repository, int? warn = null, int? blockDownload = null, int? blockDeploy = null, bool blockUnscanned = false)
    {
        using var scope = App.Services.CreateScope();
        var key = (await scope.ServiceProvider.GetRequiredService<IRepositoryService>().FindAsync(repository ?? Repository.DefaultName, TestContext.Current.CancellationToken))!.Key;
        await scope.ServiceProvider.GetRequiredService<IScanPolicyService>()
            .SaveAsync(ScanPolicyScope.Repository, key, new ScanPolicyValues(warn, blockDownload, blockDeploy, blockUnscanned), "tester", TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        App.Dispose();
        await Osv.DisposeAsync();
    }
}
