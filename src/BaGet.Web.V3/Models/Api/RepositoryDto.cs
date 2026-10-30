using System;
using BaGet.Core;

namespace BaGet.Web.V3.Models.Api;

/// <summary>A repository in the API's lists.</summary>
/// <param name="Key">The repository key.</param>
/// <param name="Name">The name (URLs and storage paths use it; it never changes).</param>
/// <param name="Format">NuGet, Npm, Maven or Docker.</param>
/// <param name="Kind">Local, Remote or Virtual.</param>
/// <param name="AnonymousRead">Whether requests without credentials may read it.</param>
/// <param name="Created">When it was created (UTC).</param>
/// <param name="PackageCount">How many package versions (artifacts) it holds.</param>
/// <param name="FeedUrl">The address a client of its format is pointed at.</param>
public sealed record RepositoryDto(int Key, string Name, RepositoryFormat Format, RepositoryKind Kind, bool AnonymousRead, DateTime Created, int PackageCount, string FeedUrl);
