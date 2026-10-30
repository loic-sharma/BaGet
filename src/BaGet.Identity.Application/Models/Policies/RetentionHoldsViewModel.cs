using System;
using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>What the legal holds screen (<c>/admin/retention/holds</c>) shows.</summary>
public sealed class RetentionHoldsViewModel : PolicyViewModel
{
    /// <summary>Only the holds of this repository (query).</summary>
    public string? Repository { get; set; }

    /// <summary>The repository of the new hold.</summary>
    public string? HoldRepository { get; set; }

    /// <summary>The artifact name of the new hold.</summary>
    public string? HoldName { get; set; }

    /// <summary>The version of the new hold; empty for every version.</summary>
    public string? HoldVersion { get; set; }

    /// <summary>Why (a case number, a ticket).</summary>
    public string? Reason { get; set; }

    /// <summary>The holds.</summary>
    public IReadOnlyList<RetentionHoldView> Holds { get; internal set; } = Array.Empty<RetentionHoldView>();

    /// <summary>The repositories a hold can name.</summary>
    public IReadOnlyList<RepositoryInfo> Repositories { get; internal set; } = Array.Empty<RepositoryInfo>();
}
