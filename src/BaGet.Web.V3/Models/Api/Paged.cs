using System.Collections.Generic;

namespace BaGet.Web.V3.Models.Api;

/// <summary>A page of a list: <c>?page=</c> (from 1) and <c>?pageSize=</c> (at most <see cref="MaxPageSize"/>).</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">This page.</param>
/// <param name="Page">The page number, from 1.</param>
/// <param name="PageSize">The size asked for.</param>
/// <param name="Total">How many items the list has.</param>
public sealed record Paged<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    /// <summary>The largest page size.</summary>
    public const int MaxPageSize = 200;

    /// <summary>The page size when none is asked for.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Whether another page follows.</summary>
    public bool HasMore => (long)Page * PageSize < Total;
}
