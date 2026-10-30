using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace BaGet.Tests;

/// <summary>
/// The upload page hands the reader a command to copy, so what it renders has to be a
/// command that works against this server.
/// </summary>
public class UploadPageTests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public UploadPageTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(output);
        _client = _app.CreateDefaultClient();
    }

    private async Task<string> GetPageAsync()
    {
        using var response = await _client.GetAsync("upload");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task OffersTheDotnetAndNuGetAndPaketCommands()
    {
        var content = await GetPageAsync();

        Assert.Contains("dotnet nuget push", content);
        Assert.Contains("nuget push -Source", content);
        Assert.Contains("paket push", content);
    }

    [Fact]
    public async Task TheDotnetCommandUsesTheServiceIndex()
    {
        var content = await GetPageAsync();

        Assert.Contains("dotnet nuget push -s http://localhost/v3/index.json", content);
    }

    [Fact]
    public async Task PowerShellGetRegistersTheV2Feed()
    {
        // PowerShellGet reads the V2 protocol. Registering the V3 service index with it
        // fails.
        var content = await GetPageAsync();

        Assert.Contains("Register-PSRepository", content);
        Assert.Contains(@"-SourceLocation \""http://localhost/api/v2\""", content);
    }

    [Fact]
    public async Task PowerShellGetDoesNotRegisterTheServiceIndex()
    {
        var content = await GetPageAsync();

        Assert.DoesNotContain(@"-SourceLocation \""http://localhost/v3/index.json\""", content);
    }

    [Fact]
    public async Task PowerShellGetPublishesToThePublishEndpoint()
    {
        var content = await GetPageAsync();

        Assert.Contains(@"-PublishLocation \""http://localhost/api/v2/package\""", content);
    }

    [Fact]
    public async Task TheAddressPowerShellGetIsToldToRegisterIsServed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Whatever the page tells the reader to register has to answer, or the
        // instructions are broken in a different way.
        await GetPageAsync();

        using var response = await _client.GetAsync("api/v2", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ThePopoverAttributeIsSpelledCorrectly()
    {
        var content = await GetPageAsync();

        Assert.Contains(@"data-toggle=""popover""", content);
        Assert.DoesNotContain("data-tottle", content);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _app?.Dispose();
    }
}

/// <summary>
/// The upload page with the V2 feed switched off.
/// </summary>
public class UploadPageWithoutV2Tests : IDisposable
{
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public UploadPageWithoutV2Tests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string>
            {
                { "Feeds:EnableV2", "false" },
            });

        _client = _app.CreateDefaultClient();
    }

    [Fact]
    public async Task PowerShellGetIsNotOffered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Its only supported protocol is not being served, so advertising it would send
        // the reader to an address that answers 404.
        using var response = await _client.GetAsync("upload", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Register-PSRepository", content);
        Assert.DoesNotContain("PowerShellGet", content);
    }

    [Fact]
    public async Task TheOtherClientsAreStillOffered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("upload", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Contains("dotnet nuget push", content);
        Assert.Contains("paket push", content);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _app?.Dispose();
    }
}