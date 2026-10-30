using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Repositories;

/// <summary>
/// The body of the create forms of the repositories screen (<c>POST /api/ui/admin/repositories</c> and its <c>create/...</c> handlers). Which fields
/// a handler reads is stated on its use case method; the others are ignored.
/// </summary>
public sealed class RepositoryCreateForm
{
    /// <summary>The name of the repository to create.</summary>
    public string? Name { get; set; }

    /// <summary>The format (<c>Npm</c>, <c>Maven</c>, <c>Docker</c>...); blank is NuGet where a format may be omitted.</summary>
    public string? Format { get; set; }

    /// <summary>The upstream of a remote repository.</summary>
    public RemoteFormModel Remote { get; set; } = new();

    /// <summary>
    /// Whether requests without credentials may read the repository. Null: the default (a local or virtual repository is readable by anyone; a remote
    /// one only when it has no credentials).
    /// </summary>
    public bool? AnonymousRead { get; set; }

    /// <summary>The administrator's password (creating a remote repository asks for it).</summary>
    public string? Password { get; set; }

    /// <summary>The format's own create fields (<see cref="FormatCreateField.Name"/> to value) of a local repository of a format other than NuGet.</summary>
    public IDictionary<string, string?>? Fields { get; set; }
}
