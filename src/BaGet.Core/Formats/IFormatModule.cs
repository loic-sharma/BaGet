using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// One package format (NuGet, npm, later Maven and OCI) as the common code sees it: what its repositories are called and where
/// they live, which kinds a repository of it can have, the UI it contributes, and how its artifacts are counted and purged.
/// A module only <em>describes</em> the format; what it does per request is a set of services registered beside it
/// (<see cref="IFormatIndexer"/>, <see cref="IFormatSearchProvider"/>, <see cref="IFormatRetentionHooks"/>,
/// <see cref="IFormatScanner"/>, looked up with <see cref="FormatServices.GetFormatService{T}"/>) and the format's own controllers,
/// whose routes go through <c>BaGetEndpointBuilder.MapRoute</c> with the module's <see cref="Format"/>.
/// </summary>
/// <remarks>
/// <para>Register a module with <c>TryAddEnumerable</c> (<c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;IFormatModule, X&gt;())</c>);
/// <see cref="FormatRegistry"/> collects them. A <see cref="RepositoryFormat"/> value without a registered module cannot be
/// created, routed or listed with artifacts. Modules are singletons and must not hold request state.</para>
/// <para><b>Routes.</b> A format's protocol is served under <see cref="RoutePrefix"/> of the repository base: for the default
/// repository <c>/{prefix}/…</c>, for the others <c>/r/{repo}/{prefix}/…</c>. NuGet predates the layer and keeps its
/// own paths (<c>v3/…</c>, <c>api/v2</c>) with an empty prefix. A request to a route of format X in a repository of another
/// format is answered 404 by <c>RepositoryResolutionMiddleware</c>.</para>
/// </remarks>
public interface IFormatModule
{
    /// <summary>The format this module serves; unique among registered modules.</summary>
    RepositoryFormat Format { get; }

    /// <summary>The name shown to people: <c>NuGet</c>, <c>npm</c>.</summary>
    string DisplayName { get; }

    /// <summary>
    /// The path segment (or segments, without slashes at the ends) every route of the format starts with, after the repository
    /// base: <c>npm</c>. Empty for NuGet, whose routes were there first.
    /// </summary>
    string RoutePrefix { get; }

    /// <summary>
    /// The address a client is configured with, relative to the repository base (<c>r/{repo}/</c>, or the root for the default
    /// repository): <c>v3/index.json</c> for NuGet, <c>npm/</c> for npm.
    /// </summary>
    string FeedPath { get; }

    /// <summary>
    /// The kinds of repository that can be created through <see cref="IRepositoryService"/> without a dedicated administration
    /// service (a virtual repository is created with its members by <see cref="IVirtualRepositoryAdministration"/> and is not
    /// listed here). Kinds not listed are refused for this format.
    /// </summary>
    IReadOnlyCollection<RepositoryKind> CreatableKinds { get; }

    /// <summary>
    /// Whether virtual repositories of this format exist (<see cref="IVirtualRepositoryAdministration.CreateAsync"/>): the format's
    /// reads then merge the members, and a write goes to the deploy repository. False for a format that has not implemented the
    /// merge.
    /// </summary>
    bool SupportsVirtual => false;

    /// <summary>
    /// Whether the format stores blobs too large to buffer (Docker layers) and so needs a store that is an
    /// <see cref="IStreamingStorageService"/>. A repository of such a format is refused at creation when the configured store
    /// cannot stream (<see cref="StreamingStorage.Require"/>).
    /// </summary>
    bool RequiresStreamingStorage => false;

    /// <summary>
    /// The top-level storage folders the format writes below a repository's <see cref="RepositoryStorage.Prefix"/>
    /// (<c>packages</c>, <c>symbols</c>…). Two modules may not claim the same folder, nor <see cref="RepositoryStorage.ReposFolder"/>:
    /// <see cref="FormatRegistry"/> refuses the registration, since a collision would let one format read another's files.
    /// </summary>
    IReadOnlyList<string> StorageFolders { get; }

    /// <summary>
    /// What the administrator fills in to create a repository of this format, on top of the name. Empty when a name is all that
    /// is needed.
    /// </summary>
    IReadOnlyList<FormatCreateField> CreateFields { get; }

    /// <summary>
    /// Turns the posted <see cref="CreateFields"/> (keyed by <see cref="FormatCreateField.Name"/>) into the repository's
    /// <see cref="Repository.Settings"/> JSON, or null for none.
    /// </summary>
    /// <exception cref="ArgumentException">A value is missing or not valid; the message is shown to the administrator.</exception>
    string? CreateSettings(IReadOnlyDictionary<string, string?> fields);

    /// <summary>The links the administrator pages show for a repository of this format (browse, upload…).</summary>
    IReadOnlyList<FormatLink> RepositoryLinks(RepositoryInfo repository);

    /// <summary>
    /// The number of artifacts (packages, tarballs…) each repository holds, by <see cref="Repository.Key"/>, over every repository
    /// (the repository filter is off). Repositories without any are left out.
    /// </summary>
    Task<IReadOnlyDictionary<int, int>> CountArtifactsAsync(IContext context, CancellationToken cancellationToken);

    /// <summary>
    /// What stops a repository of this format from being deleted: artifacts, or work in flight that would be lost.
    /// </summary>
    Task<FormatUsage> GetUsageAsync(IContext context, int repositoryKey, CancellationToken cancellationToken);

    /// <summary>
    /// Stages the removal of every database row the format keeps for the repository (the caller saves): history that dies with
    /// the repository, after <see cref="GetUsageAsync"/> found it empty.
    /// </summary>
    Task StagePurgeAsync(IContext context, int repositoryKey, CancellationToken cancellationToken);
}

/// <summary>What a repository still holds, from a format's point of view.</summary>
/// <param name="Artifacts">Stored artifacts.</param>
/// <param name="PendingWork">Whether something accepted but not finished (a submission awaiting validation) would be lost.</param>
public sealed record FormatUsage(int Artifacts, bool PendingWork);

/// <summary>An input of the create form a format contributes.</summary>
/// <param name="Name">The key in the dictionary <see cref="IFormatModule.CreateSettings"/> receives.</param>
/// <param name="Label">The visible label.</param>
/// <param name="Help">One line of explanation, or null.</param>
/// <param name="Required">Whether an empty value is refused by the form.</param>
/// <param name="Options">For a choice: the allowed values; null for free text.</param>
public sealed record FormatCreateField(string Name, string Label, string? Help = null, bool Required = false, IReadOnlyList<string>? Options = null);

/// <summary>A link a format contributes to the administration of a repository: to one of its screens or to a path under its base.</summary>
/// <remarks>Built with <see cref="ToScreen"/> or <see cref="ToPath"/>: the constructor is private, so a call written for the older
/// <c>(Id, Label, Page, Path)</c> shape fails to compile instead of putting the path in <see cref="Action"/>.</remarks>
public sealed record FormatLink
{
    private FormatLink(string id, string label, string? controller, string? action, string? path, bool needsUploads)
    {
        Id = id;
        Label = label;
        Controller = controller;
        Action = action;
        Path = path;
        NeedsUploads = needsUploads;
    }

    /// <summary>A link to a screen of the repository (<c>Home</c>/<c>Index</c>), given the repository's <c>repo</c> route value.</summary>
    public static FormatLink ToScreen(string id, string label, string controller, string action, bool needsUploads = false)
        => new(id, label, controller ?? throw new ArgumentNullException(nameof(controller)), action ?? throw new ArgumentNullException(nameof(action)), null, needsUploads);

    /// <summary>A link to a path under the repository base (<c>npm/</c>).</summary>
    public static FormatLink ToPath(string id, string label, string path, bool needsUploads = false)
        => new(id, label, null, null, path ?? throw new ArgumentNullException(nameof(path)), needsUploads);

    /// <summary>A stable identifier (<c>browse</c>, <c>upload</c>); becomes the CSS class <c>repository-{id}</c>.</summary>
    public string Id { get; }

    /// <summary>The visible text.</summary>
    public string Label { get; }

    /// <summary>The MVC controller (without the suffix) of the screen it opens; null for a path link.</summary>
    public string? Controller { get; }

    /// <summary>The action of that controller that shows the screen; null for a path link.</summary>
    public string? Action { get; }

    /// <summary>A path under the repository base; null for a screen link.</summary>
    public string? Path { get; }

    /// <summary>Shown only for repositories that take uploads (not a remote one, nor a virtual one without a deploy repository).</summary>
    public bool NeedsUploads { get; }
}
