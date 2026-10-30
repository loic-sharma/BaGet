using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Protocol.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BaGet.Tests;

/// <summary>Records every search the application runs (the virtual one and each member search it makes).</summary>
internal sealed class SearchCalls
{
    public ConcurrentQueue<(string Kind, int Skip, int Take)> Calls { get; } = new();
}

internal sealed class CountingSearchService : ISearchService
{
    private readonly ISearchService _inner;
    private readonly SearchCalls _calls;

    public CountingSearchService(ISearchService inner, SearchCalls calls)
    {
        _inner = inner;
        _calls = calls;
    }

    public IReadOnlyCollection<SearchSort> SupportedSorts => _inner.SupportedSorts;

    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        _calls.Calls.Enqueue(("search", request.Skip, request.Take));

        return _inner.SearchAsync(request, cancellationToken);
    }

    public Task<AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken)
    {
        _calls.Calls.Enqueue(("autocomplete", request.Skip, request.Take));

        return _inner.AutocompleteAsync(request, cancellationToken);
    }

    public Task<AutocompleteResponse> ListPackageVersionsAsync(VersionsRequest request, CancellationToken cancellationToken)
        => _inner.ListPackageVersionsAsync(request, cancellationToken);

    public Task<DependentsResponse> FindDependentsAsync(string? packageId, CancellationToken cancellationToken)
        => _inner.FindDependentsAsync(packageId, cancellationToken);
}

/// <summary>
/// A virtual search asks each member only for the results the page needs (<c>skip + take + 1</c>), in at most
/// <see cref="RepositorySearchService.MaxScanPerMember"/> / <see cref="RepositorySearchService.MemberPageSize"/> calls,
/// instead of reading everything; small result sets still page exactly.
/// </summary>
public class VirtualSearchBoundTests : IDisposable
{
    private const string ApiKey = "virtual-key";
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly SearchCalls _calls = new();
    private readonly BaGetApplication _app;
    private readonly HttpClient _client;

    public VirtualSearchBoundTests(ITestOutputHelper output)
    {
        _app = new BaGetApplication(
            output,
            configOverrides: new Dictionary<string, string> { { "ApiKey", ApiKey } },
            configureServices: services =>
            {
                var last = services.Last(d => d.ServiceType == typeof(ISearchService) && !d.IsKeyedService);
                services.Remove(last);
                services.AddSingleton(_calls);
                services.Add(new ServiceDescriptor(
                    typeof(ISearchService),
                    provider =>
                    {
                        var inner = last.ImplementationInstance as ISearchService
                            ?? (last.ImplementationFactory != null
                                ? (ISearchService)last.ImplementationFactory(provider)
                                : (ISearchService)ActivatorUtilities.CreateInstance(provider, last.ImplementationType!));

                        return new CountingSearchService(inner, provider.GetRequiredService<SearchCalls>());
                    },
                    last.Lifetime == ServiceLifetime.Singleton ? ServiceLifetime.Scoped : last.Lifetime));
            });
        _client = _app.CreateClient();
        _app.CreateRepositoryAsync("one").GetAwaiter().GetResult();
        _app.CreateRepositoryAsync("two").GetAwaiter().GetResult();
        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IVirtualRepositoryAdministration>()
            .CreateAsync("v", new VirtualRepositorySettings { Members = { "one", "two" }, DeployRepository = "two" }, Cancel)
            .GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    private async Task PushAsync(string repository, string id)
    {
        using var package = TestPackages.Build(id, "1.0.0", "Test package");
        using var request = TestPackages.PushRequest(package, ApiKey);
        request.RequestUri = new Uri($"r/{repository}/api/v2/package", UriKind.Relative);

        using var response = await _client.SendAsync(request, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        using var response = await _client.GetAsync(path, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel)).RootElement.Clone();
    }

    private async Task SeedAsync()
    {
        foreach (var id in new[] { "A.Pkg", "C.Pkg", "E.Pkg", "Shared.Pkg" }) await PushAsync("one", id);
        foreach (var id in new[] { "B.Pkg", "D.Pkg", "F.Pkg", "Shared.Pkg" }) await PushAsync("two", id);
    }

    [Fact]
    public async Task APageAsksEachMemberForNoMoreThanSkipPlusTakePlusOneResults()
    {
        await SeedAsync();
        var seen = new List<string?>();

        for (var skip = 0; skip < 7; skip += 2)
        {
            while (_calls.Calls.TryDequeue(out _))
            {
            }

            var page = await GetJsonAsync($"r/v/v3/search?q=&skip={skip}&take=2");
            Assert.Equal(7, page.GetProperty("totalHits").GetInt64());
            seen.AddRange(page.GetProperty("data").EnumerateArray().Select(d => d.GetProperty("id").GetString()));

            var searches = _calls.Calls.Where(c => c.Kind == "search").ToList();
            var memberCalls = searches.Skip(1).ToList();

            // The request itself, then for each member a first call of at most skip + take + 1 results, and a call that
            // finishes a member small enough to be read completely (so the total stays exact).
            Assert.InRange(searches.Count, 1 + 2, 1 + 4);
            Assert.All(memberCalls.Where(c => c.Skip == 0), c => Assert.True(c.Take <= skip + 2 + 1, $"a member was asked for {c.Take} results first for a page at skip {skip}"));
        }

        Assert.Equal(new[] { "A.Pkg", "B.Pkg", "C.Pkg", "D.Pkg", "E.Pkg", "F.Pkg", "Shared.Pkg" }, seen);
    }

    [Fact]
    public async Task ADeepPageCostsAtMostTheCapOnPageCallsPerMember()
    {
        await SeedAsync();
        while (_calls.Calls.TryDequeue(out _))
        {
        }

        var page = await GetJsonAsync("r/v/v3/search?q=&skip=1000000&take=20");
        Assert.Empty(page.GetProperty("data").EnumerateArray());

        var memberCalls = _calls.Calls.Where(c => c.Kind == "search").Skip(1).ToList();
        var cap = RepositorySearchService.MaxScanPerMember / RepositorySearchService.MemberPageSize;
        Assert.True(memberCalls.Count <= 2 * cap, $"{memberCalls.Count} member calls");
        Assert.All(memberCalls, c => Assert.True(c.Take <= RepositorySearchService.MemberPageSize));
    }

    [Fact]
    public async Task AutocompleteIsBoundedTheSameWay()
    {
        await SeedAsync();
        while (_calls.Calls.TryDequeue(out _))
        {
        }

        var page = await GetJsonAsync("r/v/v3/autocomplete?q=&skip=0&take=3");
        Assert.Equal(7, page.GetProperty("totalHits").GetInt64());
        Assert.Equal(new[] { "A.Pkg", "B.Pkg", "C.Pkg" }, page.GetProperty("data").EnumerateArray().Select(d => d.GetString()));

        var members = _calls.Calls.Where(c => c.Kind == "autocomplete").Skip(1).ToList();
        Assert.InRange(members.Count, 2, 4);
        Assert.All(members.Where(c => c.Skip == 0), c => Assert.True(c.Take <= 4, $"a member was asked for {c.Take} results"));
    }
}
