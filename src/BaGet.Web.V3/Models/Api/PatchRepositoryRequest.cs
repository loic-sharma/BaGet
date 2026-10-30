namespace BaGet.Web.V3.Models.Api;

/// <summary>A change of a repository's anonymous-read switch.</summary>
public sealed class PatchRepositoryRequest
{
    /// <summary>The new value.</summary>
    public bool? AnonymousRead { get; set; }
}
