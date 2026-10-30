using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Web.V2.Models;
using BaGet.Web.V2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace BaGet.Web.V2.Controllers;

/// <summary>
/// The NuGet V2 (OData) feed, for clients that cannot speak V3: "nuget list",
/// older Install-Package, and PowerShellGet.
/// </summary>
/// <remarks>
/// This is a deliberately partial OData implementation. The endpoints NuGet's own V2
/// client calls are served fully, but the OData query options are not interpreted:
/// "$filter", "$orderby", "$select" and "$expand" are ignored rather than applied.
/// "$skip" and "$top" are honoured, because that is how a client pages.
/// </remarks>
public class V2FeedController : Controller
{
    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 1000;

    private readonly IPackageDatabase _packages;
    private readonly ISearchService _search;
    private readonly IUrlGenerator _url;
    private readonly IOptionsSnapshot<FeedsOptions> _feeds;
    private readonly VirtualRepositoryReader? _virtual;

    public V2FeedController(
        IPackageDatabase packages,
        ISearchService search,
        IUrlGenerator url,
        IOptionsSnapshot<FeedsOptions> feeds,
        VirtualRepositoryReader? virtualReader = null)
    {
        _virtual = virtualReader;
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _feeds = feeds ?? throw new ArgumentNullException(nameof(feeds));
    }

    /// <summary>
    /// When the V2 feed is switched off the whole surface answers as though it does not
    /// exist, rather than each action checking for itself.
    /// </summary>
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!_feeds.Value.EnableV2)
        {
            context.Result = NotFound();
            return;
        }

        base.OnActionExecuting(context);
    }

    /// <summary>
    /// The service document. A V2 client reads this to discover the "Packages" set.
    /// </summary>
    public IActionResult Root()
    {
        var document =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<service xml:base=\"" + FeedBaseUrl() + "\" " +
            "xmlns=\"http://www.w3.org/2007/app\" " +
            "xmlns:atom=\"http://www.w3.org/2005/Atom\">" +
            "<workspace><atom:title>Default</atom:title>" +
            "<collection href=\"Packages\"><atom:title>Packages</atom:title></collection>" +
            "</workspace></service>";

        return Content(document, "application/xml; charset=utf-8");
    }

    public IActionResult Metadata()
    {
        return Content(V2FeedWriter.WriteMetadata(), "application/xml; charset=utf-8");
    }

    /// <summary>
    /// Every version of one package. This is the endpoint NuGet's V2
    /// FindPackageByIdResource uses to resolve a dependency.
    /// </summary>
    public async Task<IActionResult> FindPackagesById(
        [FromQuery] string id,
        [FromQuery] string semVerLevel,
        CancellationToken cancellationToken)
    {
        // The id arrives as an OData string literal: id='Newtonsoft.Json'.
        id = TrimLiteral(id);

        if (string.IsNullOrWhiteSpace(id))
        {
            return Feed(new List<V2FeedPackage>(), "FindPackagesById");
        }

        var packages = await FindAsync(id, includeUnlisted: true, cancellationToken);

        // A client that did not announce SemVer 2 support cannot parse those versions.
        var includeSemVer2 = IsSemVer2(semVerLevel);

        return Feed(
            Project(packages, p => includeSemVer2 || p.SemVerLevel != SemVerLevel.SemVer2),
            "FindPackagesById");
    }

    /// <summary>
    /// The package collection. Without a search term this lists what the feed holds.
    /// </summary>
    public async Task<IActionResult> Packages(
        [FromQuery] string id,
        [FromQuery(Name = "$skip")] int? skip,
        [FromQuery(Name = "$top")] int? top,
        [FromQuery] string semVerLevel,
        CancellationToken cancellationToken)
    {
        id = TrimLiteral(id);

        // Packages(Id='X',Version='Y') is routed here with both key values.
        if (!string.IsNullOrWhiteSpace(id))
        {
            var packages = await FindAsync(id, includeUnlisted: true, cancellationToken);

            return Feed(Project(packages), "Packages");
        }

        return await SearchFeed(
            query: null,
            skip: skip,
            top: top,
            includePrerelease: true,
            semVerLevel: semVerLevel,
            title: "Packages",
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// A single package by its key. "nuget install" resolves an exact version here.
    /// </summary>
    public async Task<IActionResult> PackagesByKey(
        string id,
        string version,
        CancellationToken cancellationToken)
    {
        id = TrimLiteral(id);
        version = TrimLiteral(version);

        if (!NuGetVersion.TryParse(version, out var parsed))
        {
            return NotFound();
        }

        var package = _virtual?.IsVirtual == true
            ? await _virtual.FindPackageAsync(id, parsed, includeUnlisted: true, cancellationToken)
            : await _packages.FindOrNullAsync(id, parsed, includeUnlisted: true, cancellationToken);
        if (package == null)
        {
            return NotFound();
        }

        // The latest-version flags are relative to the other versions, so the whole
        // set has to be projected even when only one entry is returned.
        var siblings = await FindAsync(id, includeUnlisted: true, cancellationToken);
        var projected = Project(siblings)
            .FirstOrDefault(p => p.NormalizedVersion == parsed.ToNormalizedString());

        if (projected == null)
        {
            return NotFound();
        }

        return Content(
            V2FeedWriter.WriteEntry(projected, FeedBaseUrl()),
            "application/atom+xml; type=entry; charset=utf-8");
    }

    /// <summary>
    /// The search endpoint "nuget list" calls.
    /// </summary>
    public async Task<IActionResult> Search(
        [FromQuery] string searchTerm,
        [FromQuery(Name = "$skip")] int? skip,
        [FromQuery(Name = "$top")] int? top,
        [FromQuery] bool includePrerelease,
        [FromQuery] string semVerLevel,
        CancellationToken cancellationToken)
    {
        return await SearchFeed(
            query: TrimLiteral(searchTerm),
            skip: skip,
            top: top,
            includePrerelease: includePrerelease,
            semVerLevel: semVerLevel,
            title: "Search",
            cancellationToken: cancellationToken);
    }

    private async Task<IActionResult> SearchFeed(
        string? query,
        int? skip,
        int? top,
        bool includePrerelease,
        string semVerLevel,
        string title,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(top ?? DefaultPageSize, 1, MaxPageSize);
        var offset = Math.Max(skip ?? 0, 0);

        var includeSemVer2 = IsSemVer2(semVerLevel);

        var response = await _search.SearchAsync(
            new SearchRequest
            {
                Skip = offset,
                Take = take,
                IncludePrerelease = includePrerelease,
                IncludeSemVer2 = includeSemVer2,
                Query = query,
            },
            cancellationToken);

        // Search returns one result per package id, each carrying its versions. V2
        // lists one entry per version, so the ids are re-read to get full metadata
        // (the whole page in one query when the repository is a real one).
        #pragma warning disable CS8602, CS8604
        var ids = response.Data.Select(match => match.PackageId).ToList();
        #pragma warning restore CS8602, CS8604

        var byId = new Dictionary<string, List<Package>>(StringComparer.OrdinalIgnoreCase);
        if (_virtual?.IsVirtual == true)
        {
            foreach (var id in ids)
            {
                byId[id ?? string.Empty] = (await FindAsync(id, includeUnlisted: false, cancellationToken)).ToList();
            }
        }
        else
        {
            var found = await _packages.FindManyAsync(ids!, includeUnlisted: false, cancellationToken);
            foreach (var package in found)
            {
                var key = package.Id ?? string.Empty;
                if (!byId.TryGetValue(key, out var list)) byId[key] = list = new List<Package>();
                list.Add(package);
            }
        }

        var result = new List<V2FeedPackage>();
        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id ?? string.Empty, out var versions)) continue;

            // The search matched ids; the V2 entries are versions, so the same filters apply to them.
            result.AddRange(Project(versions, p =>
                (includePrerelease || !p.IsPrerelease) && (includeSemVer2 || p.SemVerLevel != SemVerLevel.SemVer2)));
        }

        // Paging counts ids (the entries are versions): a "next" link is how V2 clients ask for the following ids.
        var more = ids.Count > 0 && response.TotalHits > offset + ids.Count;

        return Feed(result, title, more ? NextUrl(title, offset + ids.Count) : null);
    }

    // The versions of a package in the repository served. A virtual repository has none of its own: it lists the versions
    // its readable members store (each from the first member that has it), and never asks an upstream, like the rest of
    // this feed.
    private async Task<IReadOnlyList<Package>> FindAsync(string? id, bool includeUnlisted, CancellationToken cancellationToken)
        => _virtual?.IsVirtual == true
            ? await _virtual.FindPackagesAsync(id, includeUnlisted, cancellationToken)
            : await _packages.FindAsync(id, includeUnlisted, cancellationToken);

    // "include" hides versions from the client, but the latest flags stay relative to all of them.
    private List<V2FeedPackage> Project(IEnumerable<Package> packages, Func<Package, bool>? include = null)
    {
        #pragma warning disable CS8603
        return V2FeedPackage.FromPackages(
            packages,
            p => _url.GetPackageDownloadUrl(p.Id, p.Version),
            include);
        #pragma warning restore CS8603
    }

    private IActionResult Feed(IReadOnlyList<V2FeedPackage> packages, string title, string? nextUrl = null)
    {
        var baseUrl = FeedBaseUrl();
        var selfUrl = baseUrl.TrimEnd('/') + "/" + title;

        // The feed's own timestamp is the newest package it contains, so the document
        // is stable for the same content rather than changing on every request.
        var updated = packages.Count == 0
            ? DateTime.UnixEpoch
            : packages.Max(p => p.LastUpdated);

        var document = V2FeedWriter.WriteFeed(
            packages,
            feedId: selfUrl,
            feedTitle: title,
            selfUrl: selfUrl,
            baseUrl: baseUrl,
            updated: updated,
            nextUrl: nextUrl);

        return Content(document, "application/atom+xml; type=feed; charset=utf-8");
    }

    // The URL of the following page: this request's query with $skip moved on.
    private string NextUrl(string title, int skip)
    {
        var parameters = Request.Query
            .Where(q => !string.Equals(q.Key, "$skip", StringComparison.OrdinalIgnoreCase))
            .SelectMany(q => q.Value.Select(v => new KeyValuePair<string, string?>(q.Key, v)))
            .Append(new KeyValuePair<string, string?>("$skip", skip.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        return QueryHelpers.AddQueryString(FeedBaseUrl().TrimEnd('/') + "/" + title, parameters);
    }

    /// <summary>
    /// The feed's own root (<c>.../api/v2</c>), inside the request's repository: <c>IUrlGenerator</c> adds the
    /// <c>/r/{repo}/</c> prefix and the path base, so every entry link stays in the feed that served it.
    /// </summary>
    private string FeedBaseUrl()
    {
        return _url.GetV2FeedUrl()
            ?? throw new InvalidOperationException("The V2 feed's root URL could not be generated.");
    }

    /// <summary>
    /// Strip the single quotes around an OData string literal. A client may send
    /// id='X' or a bare X, and both have to work.
    /// </summary>
    internal static string TrimLiteral(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var trimmed = value.Trim();

        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'')
        {
            // An OData literal escapes an embedded quote by doubling it.
            return trimmed.Substring(1, trimmed.Length - 2).Replace("''", "'");
        }

        return trimmed;
    }

    private static bool IsSemVer2(string semVerLevel)
    {
        return NuGetVersion.TryParse(semVerLevel, out var version) && version.Major >= 2;
    }
}