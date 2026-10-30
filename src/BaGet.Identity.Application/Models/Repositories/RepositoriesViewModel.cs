using System;
using System.Collections.Generic;
using BaGet.Core;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.Models.Repositories;

/// <summary>
/// What the repositories screen (<c>/admin/repositories</c>) shows: the repositories with their feed addresses, the list filter and
/// the create forms. The posted fields of the create forms (<see cref="Name"/>, <see cref="Remote"/>) live here too, so a refused
/// creation shows again what was typed (the typed upstream password excepted: it is never sent back).
/// </summary>
public sealed class RepositoriesViewModel : ConfirmationModel, IFlashMessage
{
    private IReadOnlyDictionary<string, string> _feeds = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _bases = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, IReadOnlyList<FormatLink>> _links = new Dictionary<string, IReadOnlyList<FormatLink>>();

    /// <summary>The name for the repository to create.</summary>
    public string? Name { get; set; }

    /// <summary>The upstream settings for the remote repository to create.</summary>
    public RemoteFormModel Remote { get; set; } = new();

    /// <summary>The repositories.</summary>
    public IReadOnlyList<RepositorySummary> Repositories { get; internal set; } = Array.Empty<RepositorySummary>();

    /// <summary>Whether the database can hold more than the default repository.</summary>
    public bool CanCreate { get; internal set; }

    /// <inheritdoc />
    public string? Message { get; set; }

    /// <summary>Why the last action was refused.</summary>
    public string? Error { get; internal set; }

    /// <summary>The format to keep selected after a refused creation.</summary>
    public string? Format { get; internal set; }

    /// <summary>The format the list is narrowed to (<c>?format=</c>), or null.</summary>
    public string? FilterFormat { get; internal set; }

    /// <summary>The kind the list is narrowed to (<c>?kind=</c>), or null.</summary>
    public string? FilterKind { get; internal set; }

    /// <summary>The name fragment the list is narrowed to (<c>?q=</c>), or null.</summary>
    public string? FilterName { get; internal set; }

    /// <summary>Every format this server knows, for the list filter.</summary>
    public IReadOnlyList<RepositoryFormatChoice> AllFormats { get; internal set; } = Array.Empty<RepositoryFormatChoice>();

    /// <summary>The formats a remote repository can be created for.</summary>
    public IReadOnlyList<RepositoryFormatChoice> RemoteFormats { get; internal set; } = Array.Empty<RepositoryFormatChoice>();

    /// <summary>The formats a virtual repository can be created for.</summary>
    public IReadOnlyList<RepositoryFormatChoice> VirtualFormats { get; internal set; } = Array.Empty<RepositoryFormatChoice>();

    /// <summary>The formats other than NuGet that can be created here, with their form fields.</summary>
    public IReadOnlyList<RepositoryFormatChoice> OtherFormats { get; internal set; } = Array.Empty<RepositoryFormatChoice>();

    /// <summary>The virtual repositories that have a deploy repository, so take uploads (a remote one takes none).</summary>
    public IReadOnlyList<string> Deployable { get; internal set; } = Array.Empty<string>();

    /// <summary>The feed address of each repository, by repository name.</summary>
    public IReadOnlyDictionary<string, string> Feeds { get => _feeds; internal set => _feeds = value; }

    /// <summary>The base address of each repository (where its format's routes start, ends with a slash), by repository name.</summary>
    public IReadOnlyDictionary<string, string> BaseUrls { get => _bases; internal set => _bases = value; }

    /// <summary>The links its format contributes for each repository (browse, upload...), without the upload link where it takes none, by repository name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<FormatLink>> Links { get => _links; internal set => _links = value; }

    /// <summary>The feed address of <paramref name="repository"/>, by its format's <see cref="IFormatModule.FeedPath"/>.</summary>
    public string FeedUrl(RepositorySummary repository) => _feeds.TryGetValue(repository.Name, out var url) ? url : string.Empty;

    /// <summary>The base address of <paramref name="repository"/>, which a link given by a path (<see cref="FormatLink.Path"/>) is appended to.</summary>
    public string BaseUrl(RepositorySummary repository) => _bases.TryGetValue(repository.Name, out var url) ? url : string.Empty;

    /// <summary>The links its format contributes for <paramref name="repository"/> (browse, upload...), without the upload link where it takes none.</summary>
    public IReadOnlyList<FormatLink> LinksOf(RepositorySummary repository)
        => _links.TryGetValue(repository.Name, out var links) ? links : Array.Empty<FormatLink>();
}
