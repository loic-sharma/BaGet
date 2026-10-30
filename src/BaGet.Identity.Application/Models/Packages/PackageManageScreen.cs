using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Packages;

/// <summary>
/// What the package management screen (<c>/packages/{id}/manage</c>) shows, as plain data (the JSON model of <c>/api/ui/packages/{id}/manage</c>).
/// The page's own <c>ManageViewModel</c> is built from it for the view.
/// </summary>
public sealed class PackageManageScreen
{
    /// <summary>The package ID from the address.</summary>
    public string? Id { get; set; }

    /// <summary>The ID's owners.</summary>
    public IReadOnlyList<PackageManageOwner> Owners { get; set; } = Array.Empty<PackageManageOwner>();

    /// <summary>The ID's versions, newest first, unlisted ones included.</summary>
    public IReadOnlyList<PackageManageVersion> Versions { get; set; } = Array.Empty<PackageManageVersion>();

    /// <summary>The open abuse reports against the ID.</summary>
    public IReadOnlyList<PackageManageReport> Reports { get; set; } = Array.Empty<PackageManageReport>();

    /// <summary>The outcome of the last action.</summary>
    public string? Message { get; set; }

    /// <summary>Whether validation has submissions for the ID.</summary>
    public bool HasValidation { get; set; }

    /// <summary>Whether the ID is verified (reserved).</summary>
    public bool IsVerified => Owners.Any(o => o.IsVerified);

    /// <summary>The PDBs indexed from each version's symbol package, by normalized version.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PackageManageSymbol>> Symbols { get; set; }
        = new Dictionary<string, IReadOnlyList<PackageManageSymbol>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the database records symbols per package version.</summary>
    public bool SymbolsSupported { get; set; }

    /// <summary>The severity names a vulnerability can have, by their protocol number.</summary>
    public IReadOnlyList<string> SeverityNames { get; set; } = Array.Empty<string>();

    /// <summary>Whether the caller is an administrator.</summary>
    public bool IsAdministrator { get; set; }

    /// <summary>Whether the caller manages this ID only as an administrator (not as an owner).</summary>
    public bool ActingAsAdministrator { get; set; }

    /// <summary>Whether the caller has a password to confirm with.</summary>
    public bool HasPassword { get; set; } = true;

    /// <summary>For an account without a password: it signed in again with its provider a moment ago.</summary>
    public bool Reauthenticated { get; set; }

    /// <summary>Whether an external provider is available to sign in again with.</summary>
    public bool CanReauthenticate { get; set; }

    /// <summary>What a deletion does on this server.</summary>
    public PackageDeletionBehavior DeletionBehavior { get; set; }

    /// <summary>Whether the server refuses writes.</summary>
    public bool IsReadOnlyMode { get; set; }
}
