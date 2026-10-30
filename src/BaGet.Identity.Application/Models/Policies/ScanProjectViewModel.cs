using System.Text.Json.Serialization;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>What the scanning policy of a project (<c>/admin/scanning/projects/{name}</c>) shows.</summary>
public sealed class ScanProjectViewModel : PolicyViewModel
{
    /// <summary>The project's name (route).</summary>
    public string? Name { get; set; }

    /// <summary>The project.</summary>
    public ProjectDetails? Project { get; internal set; }

    /// <summary>The policy stored for the project, or null.</summary>
    public ScanPolicy? Own { get; internal set; }

    /// <summary>The fields of the policy editor (for the view; the JSON carries <see cref="Own"/>).</summary>
    [JsonIgnore]
    public ScanPolicyFormView PolicyForm => new(ScanPolicyFields.From(Own), Confirmation("scan-project-password"), "scan-project");
}
