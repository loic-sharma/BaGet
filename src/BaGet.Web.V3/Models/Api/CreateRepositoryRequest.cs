using System.Collections.Generic;

namespace BaGet.Web.V3.Models.Api;

/// <summary>The body that creates a repository.</summary>
public sealed class CreateRepositoryRequest
{
    /// <summary>The name (lowercase letters, digits and hyphens).</summary>
    public string? Name { get; set; }

    /// <summary><c>NuGet</c> (the default), <c>Npm</c>, <c>Maven</c> or <c>Docker</c>.</summary>
    public string? Format { get; set; }

    /// <summary><c>Local</c> (the default), <c>Remote</c> or <c>Virtual</c>.</summary>
    public string? Kind { get; set; }

    /// <summary>Whether requests without credentials may read it. Default true, except a remote repository with credentials.</summary>
    public bool? AnonymousRead { get; set; }

    /// <summary>The upstream of a remote repository.</summary>
    public RemoteSettingsRequest? Remote { get; set; }

    /// <summary>The members of a virtual repository (they can be set later).</summary>
    public VirtualRequest? Virtual { get; set; }

    /// <summary>The fields a format asks for when creating a local repository (<c>/api/v1/formats</c> lists them).</summary>
    public Dictionary<string, string?>? Fields { get; set; }
}
