using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Protocol.Models;
using Microsoft.Azure.Search;
using NuGet.Versioning;

namespace BaGet.Azure;

using QueryType = Microsoft.Azure.Search.Models.QueryType;
using SearchParameters = Microsoft.Azure.Search.Models.SearchParameters;

/// <remarks>
/// <see cref="SearchRequest.SortBy"/> is ignored (<see cref="ISearchService.SupportedSorts"/> is the
/// default, relevance only). The index could order by "totalDownloads", but "published" is not
/// sortable, and adding it means changing the index definition, which an existing index would
/// need rebuilt. Offering one of the two sorts would also make the UI inconsistent.
/// </remarks>
public class AzureSearchService : ISearchService
{
    private readonly SearchIndexClient _searchClient;
    private readonly IUrlGenerator _url;
    private readonly IFrameworkCompatibilityService _frameworks;

    public AzureSearchService(
        SearchIndexClient searchClient,
        IUrlGenerator url,
        IFrameworkCompatibilityService frameworks)
    {
        _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _frameworks = frameworks ?? throw new ArgumentNullException(nameof(frameworks));
    }

    public async Task<SearchResponse> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        var searchText = BuildSeachQuery(request.Query, request.PackageType, request.Framework);
        var filter = BuildSearchFilter(request.IncludePrerelease, request.IncludeSemVer2);
        var parameters = new SearchParameters
        {
            IncludeTotalResultCount = true,
            QueryType = QueryType.Full,
            Skip = request.Skip,
            Top = request.Take,
            Filter = filter
        };

        var response = await _searchClient.Documents.SearchAsync<PackageDocument>(
            searchText,
            parameters,
            cancellationToken: cancellationToken);

        var results = new List<SearchResult>();

        foreach (var result in response.Results)
        {
            var document = result.Document;
            var versions = new List<SearchResultVersion>();

            #pragma warning disable CS8602
            if (document.Versions.Length != document.VersionDownloads.Length)
            #pragma warning restore CS8602
            {
                throw new InvalidOperationException($"Invalid document {document.Key} with mismatched versions");
            }

            for (var i = 0; i < document.Versions.Length; i++)
            {
                var version = NuGetVersion.Parse(document.Versions[i]);

                versions.Add(new SearchResultVersion
                {
                    RegistrationLeafUrl = _url.GetRegistrationLeafUrl(document.Id, version),
                    Version = document.Versions[i],
                    Downloads = long.Parse(document.VersionDownloads[i]),
                });
            }

            #pragma warning disable CS8604
            var iconUrl = document.HasEmbeddedIcon
                ? _url.GetPackageIconDownloadUrl(document.Id, NuGetVersion.Parse(document.Version))
                : document.IconUrl;
            #pragma warning restore CS8604

            results.Add(new SearchResult
            {
                PackageId =  document.Id,
                Version = document.Version,
                Description = document.Description,
                Authors = document.Authors,
                IconUrl = iconUrl,
                LicenseUrl = document.LicenseUrl,
                ProjectUrl = document.ProjectUrl,
                RegistrationIndexUrl = _url.GetRegistrationIndexUrl(document.Id),
                Summary = document.Summary,
                Tags = document.Tags,
                Title = document.Title,
                TotalDownloads = document.TotalDownloads,
                Versions = versions
            });
        }

        #pragma warning disable CS8629
        return new SearchResponse
        {
            TotalHits = response.Count.Value,
            Data = results,
            Context = SearchContext.Default(_url.GetPackageMetadataResourceUrl())
        };
        #pragma warning restore CS8629
    }

    public async Task<AutocompleteResponse> AutocompleteAsync(
        AutocompleteRequest request,
        CancellationToken cancellationToken)
    {
        var parameters = new SearchParameters
        {
            IncludeTotalResultCount = true,
            QueryType = QueryType.Full,
            Skip = request.Skip,
            Top = request.Take,

            // The index holds one document per package id per search filter, so without this
            // the same id is returned up to four times and the total hit count is inflated.
            Filter = BuildSearchFilter(request.IncludePrerelease, request.IncludeSemVer2),
        };

        var response = await _searchClient.Documents.SearchAsync<PackageDocument>(
            BuildAutocompleteQuery(request.Query, request.PackageType),
            parameters,
            cancellationToken: cancellationToken);

        var results = response.Results
            .Select(r => r.Document.Id)
            .ToList()
            .AsReadOnly();

        #pragma warning disable CS8629
        return new AutocompleteResponse
        {
            TotalHits = response.Count.Value,
            Data = results,
            Context = AutocompleteContext.Default
        };
        #pragma warning restore CS8629
    }

    public async Task<AutocompleteResponse> ListPackageVersionsAsync(
        VersionsRequest request,
        CancellationToken cancellationToken)
    {
        var filters = BuildSearchFilter(request.IncludePrerelease, request.IncludeSemVer2);

        // The exact lookup, for documents that have "normalizedId".
        var response = await _searchClient.Documents.SearchAsync<PackageDocument>(
            "*",
            new SearchParameters { Top = 1, Filter = BuildVersionsFilter(request.PackageId, filters) },
            cancellationToken: cancellationToken);

        var document = response.Results.Select(r => r.Document).FirstOrDefault();

        // A document indexed before "normalizedId" existed: a phrase query on "id" finds
        // it among the IDs containing the same words, and the exact match is picked here.
        if (document == null)
        {
            response = await _searchClient.Documents.SearchAsync<PackageDocument>(
                $"id:{EscapeQueryPhrase(request.PackageId)}",
                new SearchParameters { QueryType = QueryType.Full, Top = 50, Filter = filters },
                cancellationToken: cancellationToken);

            document = response.Results
                .Select(r => r.Document)
                .FirstOrDefault(d => string.Equals(d.Id, request.PackageId, StringComparison.OrdinalIgnoreCase));
        }

        var versions = document?.Versions ?? Array.Empty<string>();

        return new AutocompleteResponse
        {
            TotalHits = versions.Length,
            Data = versions,
            Context = AutocompleteContext.Default,
        };
    }

    /// <summary>
    /// The OData filter for one package ID's document under these search filters.
    /// </summary>
    internal static string BuildVersionsFilter(string? packageId, string searchFilter)
    {
        if (packageId == null) throw new ArgumentNullException(nameof(packageId));

        // An OData string literal escapes a quote by doubling it.
        var literal = packageId.ToLowerInvariant().Replace("'", "''");

        return $"normalizedId eq '{literal}' and {searchFilter}";
    }

    /// <summary>
    /// The full Lucene query for autocomplete: a prefix match on the whole ID, or, for a
    /// document indexed before "normalizedId" existed, the analyzed match autocomplete
    /// always made; plus the package type, if one was asked for.
    /// </summary>
    internal static string BuildAutocompleteQuery(string? query, string? packageType)
    {
        var builder = new StringBuilder();

        var term = EscapeQueryTerm((query ?? string.Empty).Trim().TrimEnd('*').ToLowerInvariant());
        if (term.Length > 0)
        {
            builder.Append("+(normalizedId:");
            builder.Append(term);
            builder.Append("* OR ");
            builder.Append(term);
            builder.Append(')');
        }

        if (!string.IsNullOrEmpty(packageType))
        {
            builder.Append(" +packageTypes:");
            builder.Append(EscapeQueryPhrase(packageType));
        }

        var result = builder.ToString().Trim();

        return result.Length == 0 ? "*" : result;
    }

    public async Task<DependentsResponse> FindDependentsAsync(
        string? packageId,
        CancellationToken cancellationToken)
    {
        var query = BuildDependentsQuery(packageId);
        var parameters = new SearchParameters
        {
            IncludeTotalResultCount = true,
            QueryType = QueryType.Full,
            Skip = 0,
            Top = 20,
        };

        var response = await _searchClient.Documents.SearchAsync<PackageDocument>(query, parameters, cancellationToken: cancellationToken);
        var results = response.Results
            .Select(r => new PackageDependent
            {
                Id = r.Document.Id,
                Description = r.Document.Description,
                TotalDownloads = r.Document.TotalDownloads
            })
            .ToList()
            .AsReadOnly();

        #pragma warning disable CS8629
        return new DependentsResponse
        {
            TotalHits = response.Count.Value,
            Data = results
        };
        #pragma warning restore CS8629
    }

    /// <summary>
    /// Builds the query that finds the packages which depend on <paramref name="packageId"/>.
    /// It runs with <see cref="QueryType.Full"/>, the full Lucene query syntax.
    /// </summary>
    internal static string BuildDependentsQuery(string? packageId)
    {
        if (packageId == null)
        {
            throw new ArgumentNullException(nameof(packageId));
        }

        // "dependencies" is indexed with ExactMatchCustomAnalyzer (a keyword tokenizer plus a
        // lowercase filter) and holds lowercased ids, so the whole id must reach the analyzer
        // as a single entity.
        return $"dependencies:{EscapeQueryPhrase(packageId.ToLowerInvariant())}";
    }

    /// <summary>
    /// Turns <paramref name="value"/> into a quoted phrase, which the full Lucene query parser
    /// evaluates as a single literal entity, so that the value cannot change the meaning of the
    /// query it is embedded in.
    /// </summary>
    /// <remarks>
    /// In an unquoted term the full Lucene syntax treats
    /// <c>+ - &amp; | ! ( ) { } [ ] ^ " ~ * ? : \ /</c> as operators, and whitespace splits one
    /// term into several - whitespace is not in that list and cannot be escaped, yet it is what
    /// lets <c>AND</c>, <c>OR</c> and <c>NOT</c> become operators. Quoting solves both at once:
    /// placement decides whether a symbol is an operator, and inside quotation marks every
    /// character is content (wildcard, fuzzy, proximity and regex constructs are documented as
    /// applying to a term, not to a phrase). That leaves exactly two significant characters,
    /// each escaped here with a single backslash, the documented escape:
    /// <list type="bullet">
    /// <item><description><c>"</c>, which would end the phrase early;</description></item>
    /// <item><description><c>\</c>, the escape character itself, which would otherwise swallow
    /// the character after it - a trailing one would escape the closing quotation mark.</description></item>
    /// </list>
    /// Neither is legal in a NuGet package id, so an ordinary id is emitted verbatim between
    /// quotation marks and no backslash ever reaches the parser for a real id.
    /// See https://learn.microsoft.com/azure/search/query-lucene-syntax.
    /// </remarks>
    /// <summary>
    /// Escapes the characters that are syntax in the full Lucene query the search runs
    /// with, leaving the value usable as a term. Unlike <see cref="EscapeQueryPhrase"/>
    /// this does not quote, so a caller may still append a trailing "*" to make the term
    /// a prefix query.
    /// </summary>
    /// <remarks>
    /// See the escaping rules for the full Lucene syntax:
    /// https://learn.microsoft.com/en-us/azure/search/query-lucene-syntax
    /// </remarks>
    internal static string EscapeQueryTerm(string? value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        const string special = @"+-&|!(){}[]^""~*?:\/";
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (special.IndexOf(character) >= 0)
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    internal static string EscapeQueryPhrase(string? value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var builder = new StringBuilder(value.Length + 2);

        builder.Append('"');

        foreach (var character in value)
        {
            if (character == '"' || character == '\\')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        builder.Append('"');

        return builder.ToString();
    }

    private string BuildSeachQuery(string? query, string? packageType, string? framework)
    {
        var frameworks = string.IsNullOrEmpty(framework)
            ? null
            : _frameworks.FindAllCompatibleFrameworks(framework);

        return BuildSearchQuery(query, packageType, frameworks);
    }

    /// <summary>
    /// Builds the full Lucene query. Every caller supplied value is escaped: the search
    /// runs with <see cref="QueryType.Full"/>, so an unescaped value would change the
    /// meaning of the query rather than being matched against.
    /// </summary>
    internal static string BuildSearchQuery(
        string? query,
        string? packageType,
        IReadOnlyList<string>? frameworks)
    {
        var queryBuilder = new StringBuilder();

        if (!string.IsNullOrEmpty(query))
        {
            // Escaped as a term, not quoted as a phrase, so the "*" below still makes
            // this a prefix query.
            queryBuilder.Append(EscapeQueryTerm(query.TrimEnd().TrimEnd('*')));
            queryBuilder.Append('*');
        }

        if (!string.IsNullOrEmpty(packageType))
        {
            queryBuilder.Append(" +packageTypes:");
            queryBuilder.Append(EscapeQueryPhrase(packageType));
        }

        if (frameworks != null && frameworks.Count > 0)
        {
            // An unrecognised moniker is returned as given by FindAllCompatibleFrameworks,
            // so these values are caller controlled too.
            queryBuilder.Append(" +frameworks:(");
            queryBuilder.Append(string.Join(" ", frameworks.Select(EscapeQueryPhrase)));
            queryBuilder.Append(')');
        }

        return queryBuilder.ToString();
    }

    /// <summary>
    /// Builds the OData filter that selects the single document matching these search filters.
    /// <see cref="IndexActionBuilder"/> writes one document per <see cref="SearchFilters"/>
    /// value, using the same enum name as its "searchFilters" field.
    /// </summary>
    internal static string BuildSearchFilter(bool includePrerelease, bool includeSemVer2)
    {
        var searchFilters = SearchFilters.Default;

        if (includePrerelease)
        {
            searchFilters |= SearchFilters.IncludePrerelease;
        }

        if (includeSemVer2)
        {
            searchFilters |= SearchFilters.IncludeSemVer2;
        }

        return $"searchFilters eq '{searchFilters}'";
    }
}