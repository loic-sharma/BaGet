namespace BaGet.Core;

/// <summary>
/// What a request, job or command needs to know about the repository it works in: immutable, and small enough to
/// copy around.
/// </summary>
/// <param name="Key">The <see cref="Repository.Key"/>.</param>
/// <param name="Name">The <see cref="Repository.Name"/>.</param>
/// <param name="Format">The package format served.</param>
/// <param name="Kind">How the repository gets its content.</param>
public sealed record RepositoryInfo(int Key, string Name, RepositoryFormat Format, RepositoryKind Kind)
{
    /// <summary>
    /// The repository served at <c>/</c>.
    /// </summary>
    public static RepositoryInfo Default { get; } =
        new(Repository.DefaultKey, Repository.DefaultName, RepositoryFormat.NuGet, RepositoryKind.Local);

    /// <summary>
    /// Whether this is the <see cref="Repository.DefaultName"/> repository.
    /// </summary>
    public bool IsDefault => Key == Repository.DefaultKey;

    /// <summary>
    /// The path in front of this repository's routes, feeds and pages, without a leading slash: nothing for the
    /// default repository (served at <c>/</c>), else <c>r/{name}/</c>.
    /// </summary>
    public string PathPrefix => IsDefault ? string.Empty : "r/" + System.Uri.EscapeDataString(Name) + "/";
}
