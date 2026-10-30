using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Core;
using BaGet.Identity.Application.Models.Packages;

namespace BaGet.Identity.Models.Packages;

/// <summary>
/// What the package management screen (<c>/packages/{id}/manage</c>) shows to the view: built from the screen's plain model
/// (<see cref="PackageManageScreen"/>, the JSON model of the twin) so a host's view override keeps the entities it was written against.
/// </summary>
public sealed class ManageViewModel
{
    /// <summary>The view's model for what the screen's use case produced.</summary>
    internal static ManageViewModel From(PackageManageScreen screen)
    {
        if (screen == null) throw new ArgumentNullException(nameof(screen));

        var model = new ManageViewModel
        {
            Id = screen.Id,
            Message = screen.Message,
            HasValidation = screen.HasValidation,
            SymbolsSupported = screen.SymbolsSupported,
            SeverityNames = screen.SeverityNames,
            IsAdministrator = screen.IsAdministrator,
            ActingAsAdministrator = screen.ActingAsAdministrator,
            HasPassword = screen.HasPassword,
            Reauthenticated = screen.Reauthenticated,
            CanReauthenticate = screen.CanReauthenticate,
            DeletionBehavior = screen.DeletionBehavior,
            IsReadOnlyMode = screen.IsReadOnlyMode,
            Owners = screen.Owners.Select(o => new PackageOwner { PackageId = screen.Id, OwnerName = o.OwnerName, IsVerified = o.IsVerified }).ToList(),
            Versions = screen.Versions.Select(v => new Package
            {
                Id = screen.Id,
                NormalizedVersionString = v.NormalizedVersion,
                OriginalVersionString = v.Version,
                Downloads = v.Downloads,
                Listed = v.Listed,
                Deprecation = v.Deprecation == null
                    ? null
                    : new PackageDeprecation
                    {
                        Reasons = v.Deprecation.Reasons.ToArray(),
                        Message = v.Deprecation.Message,
                        AlternatePackageId = v.Deprecation.AlternatePackageId,
                        AlternateVersionRange = v.Deprecation.AlternateVersionRange,
                    },
                Vulnerabilities = v.Vulnerabilities
                    .Select(x => new PackageVulnerability { Key = x.Key, AdvisoryUrl = x.AdvisoryUrl, Severity = x.Severity, VersionRange = x.VersionRange, Source = x.Source })
                    .ToList(),
            }).ToList(),
            Reports = screen.Reports
                .Select(r => new AbuseReport { Key = r.Key, PackageId = screen.Id, PackageVersion = r.PackageVersion, Reason = r.Reason, Details = r.Details, ReporterEmail = r.ReporterEmail })
                .ToList(),
            Symbols = screen.Symbols.ToDictionary(
                p => p.Key,
                p => (IReadOnlyList<SymbolReference>)p.Value.Select(s => new SymbolReference(s.FileName, s.Key)).ToList(),
                StringComparer.OrdinalIgnoreCase),
        };

        return model;
    }

    /// <summary>The package ID from the address.</summary>
    public string? Id { get; internal set; }

    /// <summary>The ID's owners.</summary>
    public IReadOnlyList<PackageOwner> Owners { get; internal set; } = Array.Empty<PackageOwner>();

    /// <summary>The ID's versions, newest first, unlisted ones included.</summary>
    public IReadOnlyList<Package> Versions { get; internal set; } = Array.Empty<Package>();

    /// <summary>The open abuse reports against the ID.</summary>
    public IReadOnlyList<AbuseReport> Reports { get; internal set; } = Array.Empty<AbuseReport>();

    /// <summary>The outcome of the last action.</summary>
    public string? Message { get; internal set; }

    /// <summary>Whether validation has submissions for the ID (the page links to their status).</summary>
    public bool HasValidation { get; internal set; }

    /// <summary>Whether the ID is verified (reserved): an owner row carries the flag.</summary>
    public bool IsVerified => Owners.Any(o => o.IsVerified);

    /// <summary>The PDBs indexed from each version's symbol package, by normalized version (case-insensitive).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SymbolReference>> Symbols { get; internal set; }
        = new Dictionary<string, IReadOnlyList<SymbolReference>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the database records symbols per package version (false for Azure Table).</summary>
    public bool SymbolsSupported { get; internal set; }

    /// <summary>The severity names a vulnerability can have, by their protocol number.</summary>
    public IReadOnlyList<string> SeverityNames { get; internal set; } = Array.Empty<string>();

    /// <summary>Whether the signed in user is an administrator.</summary>
    public bool IsAdministrator { get; internal set; }

    /// <summary>Whether the user manages this ID only as an administrator (not as an owner).</summary>
    public bool ActingAsAdministrator { get; internal set; }

    /// <summary>Whether the signed in administrator has a password to confirm with.</summary>
    public bool HasPassword { get; internal set; } = true;

    /// <summary>For an account without a password: it signed in again with its provider a moment ago.</summary>
    public bool Reauthenticated { get; internal set; }

    /// <summary>Whether an external provider is available to sign in again with.</summary>
    public bool CanReauthenticate { get; internal set; }

    /// <summary>What a deletion does on this server.</summary>
    public PackageDeletionBehavior DeletionBehavior { get; internal set; }

    /// <summary>Whether the server refuses writes.</summary>
    public bool IsReadOnlyMode { get; internal set; }

    /// <summary>Whether a deletion removes the rows and files (rather than unlisting).</summary>
    public bool DeletionIsPermanent => DeletionBehavior == PackageDeletionBehavior.HardDelete;

    /// <summary>The label of the delete button.</summary>
    public string DeleteLabel => DeletionIsPermanent
        ? "Delete this version permanently"
        : "Unlist this version";

    /// <summary>What the delete button does, in words.</summary>
    public string DeleteCaption => DeletionIsPermanent
        ? "Removes the database rows and the .nupkg, .nuspec, readme and icon from storage. "
          + "Consumers can no longer restore it. Indexed PDBs keep being served, and an "
          + "external search index keeps the version until it is reindexed."
        : "Hidden from search and from the version list; anyone who knows the exact version "
          + "can still restore it.";
}
