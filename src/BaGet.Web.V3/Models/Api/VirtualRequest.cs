using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Web.V3.Models.Api;

/// <summary>A virtual repository's members, as the API takes them.</summary>
public sealed class VirtualRequest
{
    /// <summary>The member repositories' names in resolution order.</summary>
    public List<string> Members { get; set; } = new();

    /// <summary>The member that receives deployments, or null for none.</summary>
    public string? DeployRepository { get; set; }

    internal VirtualRepositorySettings ToSettings() => new() { Members = Members ?? new List<string>(), DeployRepository = string.IsNullOrWhiteSpace(DeployRepository) ? null : DeployRepository };
}
