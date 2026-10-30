using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Protocol.Models;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;

namespace BaGet.Core;

/// <summary>
/// The search the protocol endpoints and pages use: the configured <see cref="ISearchService"/> for an ordinary
/// repository, and for a virtual one the merge of its readable members' searches.
/// </summary>
/// <remarks>
/// <para>
/// A virtual search reads each member in its own order, page by page, only as far as the page asked for needs: the first
/// <c>skip + take + 1</c> results of each member (at most <see cref="MaxScanPerMember"/>, that is at most
/// <see cref="MaxScanPerMember"/> / <see cref="MemberPageSize"/> page calls per member and request), so a request for the
/// first page costs one small call per member however large the members are (a member of at most
/// <see cref="ExactScanPerMember"/> results is then read to its end, at most that many results in
/// <see cref="ExactScanPerMember"/> / <see cref="MemberPageSize"/> page calls, so small and medium installs keep exact
/// totals). The windows are then merged once. A package id
/// found in several members is one result: the first member entry, with the versions of the others added and the download
/// counts summed. The order is total and does not depend on the page asked for: <see cref="SearchSort.Relevance"/> is the
/// id (case-insensitive), <see cref="SearchSort.TotalDownloads"/> is the summed downloads then the id.
/// </para>
/// <para>
/// Exactness: the page is exact whenever each member window covers it, which holds for <see cref="SearchSort.Relevance"/>
/// (a member lists ids in ascending order, so an id of the merged first <c>n</c> is in its member first <c>n</c>; databases
/// that order mixed-case ids differently from the case-insensitive merge can misplace one at a window edge) and for every
/// result set that fits a window. For <see cref="SearchSort.TotalDownloads"/> an id held by several members is summed from
/// the entries inside each member window, so an id near the edge of one window can rank slightly lower than its full
/// total says. <c>totalHits</c> is the number of distinct ids when every member was read to its end (always the case when each
/// has at most <see cref="ExactScanPerMember"/> results), else an estimate
/// (the ids seen plus each member unseen remainder, an upper bound); results deeper than <see cref="MaxScanPerMember"/>
/// per member are cut off. <see cref="SearchSort.LastUpdated"/> is not offered (results carry no date).
/// </para>
/// <para>Autocomplete and the dependents list are merged the same way (ids ordered by name).</para>
/// </remarks>
public sealed class RepositorySearchService : ISearchService
{
    /// <summary>Page size used to read a member.</summary>
    internal const int MemberPageSize = 500;

    /// <summary>A member with at most this many results is always read completely, which keeps the merged total exact.</summary>
    public const int ExactScanPerMember = 2_000;

    /// <summary>Most results read from one member for one query (so also the deepest page a virtual search can serve).</summary>
    public const int MaxScanPerMember = 10_000;

    private static readonly IReadOnlyCollection<SearchSort> VirtualSorts = new[] { SearchSort.Relevance, SearchSort.TotalDownloads };

    private readonly ISearchService _inner;
    private readonly VirtualRepositoryReader _reader;
    private readonly ISearchResponseBuilder _builder;

    public RepositorySearchService(ISearchService inner, VirtualRepositoryReader reader, ISearchResponseBuilder builder)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<SearchSort> SupportedSorts => _reader.IsVirtual ? VirtualSorts : _inner.SupportedSorts;

    /// <inheritdoc/>
    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        if (!_reader.IsVirtual) return await _inner.SearchAsync(request, cancellationToken).ConfigureAwait(false);

        var sort = request.SortBy == SearchSort.TotalDownloads ? SearchSort.TotalDownloads : SearchSort.Relevance;
        var skip = Math.Max(request.Skip, 0);
        var take = Math.Max(request.Take, 0);
        var window = Window(skip, take);
        var members = await _reader.AllAsync(
            packageId: null,
            sp => DrainSearchAsync(sp.GetRequiredService<ISearchService>(), request, sort, window, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var merged = Merge(members.Select(m => m.Value.Items), sort);
        var remainder = members.Sum(m => Math.Max(0, m.Value.Total - m.Value.Items.Count));

        var response = _builder.BuildSearch(Array.Empty<PackageRegistration>(), merged.Count + remainder);
        response.Data = merged.Skip(skip).Take(take).ToList();

        return response;
    }

    /// <inheritdoc/>
    public async Task<AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken)
    {
        if (!_reader.IsVirtual) return await _inner.AutocompleteAsync(request, cancellationToken).ConfigureAwait(false);

        var window = Window(Math.Max(request.Skip, 0), Math.Max(request.Take, 0));
        var members = await _reader.AllAsync(
            packageId: null,
            sp => DrainAutocompleteAsync(sp.GetRequiredService<ISearchService>(), request, window, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var ids = DistinctIgnoreCase(members.SelectMany(m => m.Value.Items))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(id => id, StringComparer.Ordinal)
            .ToList();
        var remainder = members.Sum(m => Math.Max(0, m.Value.Total - m.Value.Items.Count));

        return _builder.BuildAutocomplete(
            ids.Skip(Math.Max(request.Skip, 0)).Take(Math.Max(request.Take, 0)).ToList(),
            ids.Count + remainder);
    }

    /// <inheritdoc/>
    public async Task<AutocompleteResponse> ListPackageVersionsAsync(VersionsRequest request, CancellationToken cancellationToken)
    {
        if (!_reader.IsVirtual) return await _inner.ListPackageVersionsAsync(request, cancellationToken).ConfigureAwait(false);

        var members = await _reader.AllAsync(
            request.PackageId,
            sp => sp.GetRequiredService<ISearchService>().ListPackageVersionsAsync(request, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var versions = DistinctIgnoreCase(members.SelectMany(m => m.Value.Data ?? Array.Empty<string?>()))
            .OrderBy(v => NuGetVersion.TryParse(v, out var parsed) ? parsed : null, Comparer<NuGetVersion?>.Create(CompareVersions))
            .ThenBy(v => v, StringComparer.Ordinal)
            .ToList();

        return _builder.BuildAutocomplete(versions, versions.Count);
    }

    /// <inheritdoc/>
    public async Task<DependentsResponse> FindDependentsAsync(string? packageId, CancellationToken cancellationToken)
    {
        if (!_reader.IsVirtual) return await _inner.FindDependentsAsync(packageId, cancellationToken).ConfigureAwait(false);

        var members = await _reader.AllAsync(
            packageId: null,
            sp => sp.GetRequiredService<ISearchService>().FindDependentsAsync(packageId, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var dependents = members
            .SelectMany(m => m.Value.Data ?? Array.Empty<PackageDependent>())
            .GroupBy(d => d.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PackageDependent { Id = g.First().Id, Description = g.First().Description, TotalDownloads = g.Sum(d => d.TotalDownloads) })
            .OrderByDescending(d => d.TotalDownloads)
            .ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        return _builder.BuildDependents(dependents);
    }

    private static int CompareVersions(NuGetVersion? a, NuGetVersion? b)
        => a == null ? (b == null ? 0 : 1) : b == null ? -1 : a.CompareTo(b);

    private static IEnumerable<string> DistinctIgnoreCase(IEnumerable<string?> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (value != null && seen.Add(value)) yield return value;
        }
    }

    /// <summary>
    /// The search results of several members as one ordered list of distinct package ids. The earliest member's entry
    /// stands for an id found in several; the others add their versions and downloads.
    /// </summary>
    internal static List<SearchResult> Merge(IEnumerable<IReadOnlyList<SearchResult>> members, SearchSort sort)
    {
        var byId = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
        var order = new List<SearchResult>();

        foreach (var result in members.SelectMany(m => m))
        {
            if (result.PackageId == null) continue;

            if (!byId.TryGetValue(result.PackageId, out var winner))
            {
                byId[result.PackageId] = result;
                order.Add(result);
                continue;
            }

            winner.TotalDownloads += result.TotalDownloads;
            AddVersions(winner, result);
        }

        var ordered = sort == SearchSort.TotalDownloads
            ? order.OrderByDescending(r => r.TotalDownloads)
                .ThenBy(r => r.PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.PackageId, StringComparer.Ordinal)
            : order.OrderBy(r => r.PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.PackageId, StringComparer.Ordinal);

        return ordered.ToList();
    }

    // The winner lists every version of the id once (highest first, as a single repository does) and reports the highest.
    private static void AddVersions(SearchResult winner, SearchResult other)
    {
        var versions = new List<SearchResultVersion>(winner.Versions ?? Array.Empty<SearchResultVersion>());
        var known = new HashSet<string>(versions.Select(v => v.Version ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        foreach (var version in other.Versions ?? Array.Empty<SearchResultVersion>())
        {
            if (known.Add(version.Version ?? string.Empty)) versions.Add(version);
        }

        versions = versions
            .OrderByDescending(v => NuGetVersion.TryParse(v.Version, out var parsed) ? parsed : null, Comparer<NuGetVersion?>.Create(CompareVersions))
            .ToList();

        winner.Versions = versions;
        if (versions.Count > 0) winner.Version = versions[0].Version;
    }

    // How many results of each member the page [skip, skip + take) needs, plus one to see that there are more.
    private static int Window(int skip, int take)
        => (int)Math.Min((long)skip + take + 1, MaxScanPerMember);

    private static async Task<Drained<SearchResult>> DrainSearchAsync(ISearchService search, SearchRequest request, SearchSort sort, int window, CancellationToken cancellationToken)
    {
        var items = new List<SearchResult>();
        long total = 0;

        var limit = window;
        while (items.Count < limit)
        {
            var take = Math.Min(MemberPageSize, limit - items.Count);
            var page = await search.SearchAsync(
                new SearchRequest
                {
                    Skip = items.Count,
                    Take = take,
                    IncludePrerelease = request.IncludePrerelease,
                    IncludeSemVer2 = request.IncludeSemVer2,
                    PackageType = request.PackageType,
                    Framework = request.Framework,
                    Query = request.Query,
                    SortBy = sort,
                },
                cancellationToken).ConfigureAwait(false);

            total = page.TotalHits;

            // A member that is small anyway is read to its end, so its total (and the merged one) stays exact.
            if (total <= ExactScanPerMember) limit = Math.Max(limit, (int)total);
            var data = page.Data ?? Array.Empty<SearchResult>();
            items.AddRange(data);

            if (data.Count < take || items.Count >= total) break;
        }

        return new Drained<SearchResult>(items, Math.Max(total, items.Count));
    }

    private static async Task<Drained<string?>> DrainAutocompleteAsync(ISearchService search, AutocompleteRequest request, int window, CancellationToken cancellationToken)
    {
        var items = new List<string?>();
        long total = 0;

        var limit = window;
        while (items.Count < limit)
        {
            var take = Math.Min(MemberPageSize, limit - items.Count);
            var page = await search.AutocompleteAsync(
                new AutocompleteRequest
                {
                    Skip = items.Count,
                    Take = take,
                    IncludePrerelease = request.IncludePrerelease,
                    IncludeSemVer2 = request.IncludeSemVer2,
                    PackageType = request.PackageType,
                    Query = request.Query,
                },
                cancellationToken).ConfigureAwait(false);

            total = page.TotalHits;
            if (total <= ExactScanPerMember) limit = Math.Max(limit, (int)total);
            var data = page.Data ?? Array.Empty<string?>();
            items.AddRange(data);

            if (data.Count < take || items.Count >= total) break;
        }

        return new Drained<string?>(items, Math.Max(total, items.Count));
    }

    private sealed record Drained<T>(List<T> Items, long Total);
}
