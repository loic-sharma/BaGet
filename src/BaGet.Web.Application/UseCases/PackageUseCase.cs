using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using BaGet.Identity.Application.Links;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;
using BaGet.Web.Application.Models.Packages;
using BaGet.Web.Application.Screens;
using BaGet.Web.Application.Snippets;

namespace BaGet.Web.Application.UseCases;

/// <summary>
/// The package page (<c>/packages/{id}/{version?}</c>, and under <c>/r/{repo}/</c>): finds the version to show (the requested one, else
/// the newest listed) and gathers what the page displays. The access rule on the package (Read) is the repository check of the screen.
/// </summary>
public sealed class PackageUseCase
{
    private readonly IPackageService _packages;
    private readonly IPackageContentService _content;
    private readonly ISearchService _search;
    private readonly IUrlGenerator _url;
    private readonly IServiceProvider _services;

    /// <summary>Creates the use case.</summary>
    /// <param name="packages">The packages of the repository.</param>
    /// <param name="content">The package files.</param>
    /// <param name="search">Finds the dependents.</param>
    /// <param name="url">Builds the download addresses.</param>
    /// <param name="services">Finds the services that exist only when accounts are enabled (owners, avatars, the Manage rule).</param>
    public PackageUseCase(
        IPackageService packages,
        IPackageContentService content,
        ISearchService search,
        IUrlGenerator url,
        IServiceProvider services)
    {
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>Loads the page of <paramref name="id"/> (and <paramref name="version"/>, else the newest listed one).</summary>
    /// <param name="id">The package ID.</param>
    /// <param name="version">The requested version, or null for the newest listed one.</param>
    /// <param name="caller">The signed in user (decides whether the Manage link is offered); null or anonymous for a visitor.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    public async Task<ScreenResult<PackageScreen>> GetAsync(
        string id,
        string? version,
        ClaimsPrincipal? caller,
        CancellationToken cancellationToken)
    {
        var packages = await _packages.FindPackagesAsync(id, cancellationToken);
        var listedPackages = packages.Where(p => p.Listed).ToList();

        // Try to find the requested version.
        Package? package = null;
        if (NuGetVersion.TryParse(version, out var requestedVersion))
        {
            package = packages.SingleOrDefault(p => p.Version == requestedVersion);
        }

        // Otherwise try to display the latest version.
        if (package == null)
        {
            package = listedPackages.OrderByDescending(p => p.Version).FirstOrDefault();
        }

        if (package == null)
        {
            return ScreenResult<PackageScreen>.Ok(new PackageScreen { Found = false, Package = new PackageDetails { Id = id } });
        }

        var packageVersion = package.Version;
        var isTemplate = package.PackageTypes?.Any(t => t.Name?.Equals("Template", StringComparison.OrdinalIgnoreCase) == true) ?? false;
        var isTool = package.PackageTypes?.Any(t => t.Name?.Equals("DotnetTool", StringComparison.OrdinalIgnoreCase) == true) ?? false;

        var dependents = await _search.FindDependentsAsync(package.Id, cancellationToken);

        string? readme = null;
        if (package.HasReadme)
        {
            readme = await GetReadmeHtmlOrNullAsync(package.Id, packageVersion, cancellationToken);
        }

        var screen = new PackageScreen
        {
            Found = true,
            Package = new PackageDetails
            {
                Id = package.Id,
                Version = package.NormalizedVersionString,
                Description = package.Description,
                ReleaseNotes = package.ReleaseNotes,
                ProjectUrl = package.ProjectUrlString,
                RepositoryUrl = package.RepositoryUrlString,
                Downloads = package.Downloads,
                Authors = package.Authors ?? Array.Empty<string>(),
            },
            IsDotnetTemplate = isTemplate,
            IsDotnetTool = isTool,
            LastUpdated = packages.Max(p => p.Published),
            TotalDownloads = packages.Sum(p => p.Downloads),
            UsedBy = dependents.Data ?? (IReadOnlyList<PackageDependent>)Array.Empty<PackageDependent>(),
            DependencyGroups = PackageDependencies.GroupByFramework(package.Dependencies),
            Versions = ToVersions(listedPackages, packageVersion),
            ReadmeHtml = readme,
            IconUrl = package.HasEmbeddedIcon
                ? _url.GetPackageIconDownloadUrl(package.Id, packageVersion)
                : package.IconUrlString,
            LicenseUrl = GetLicenseUrlOrNull(package),
            FrameworkGroups = PackageFrameworks.Compute(package.TargetFrameworks?.Select(f => f.Moniker)),
            InstallTabs = InstallSnippets.Build(package.Id, package.NormalizedVersionString, isTemplate, isTool),
            PackageDownloadUrl = _url.GetPackageDownloadUrl(package.Id, packageVersion),
            PackageExplorerUrl = "https://nuget.info/packages/"
                + Uri.EscapeDataString(package.Id ?? string.Empty) + "/" + Uri.EscapeDataString(package.NormalizedVersionString ?? string.Empty),
        };

        // PackageOwnershipService is only registered when accounts are enabled.
        var ownership = _services.GetService<PackageOwnershipService>();
        if (ownership != null)
        {
            var owners = (await ownership.GetOwnersAsync(package.Id, cancellationToken)).Select(o => o.OwnerName).ToList();
            var avatars = await _services.GetRequiredService<AvatarService>().GetUrlsAsync(owners, 32, cancellationToken);
            string? manageUrl = null;

            // A virtual repository holds no packages to manage: they are managed in the member that stores them.
            var repository = _services.GetService<IRepositoryContext>()?.Current;
            if (caller?.Identity?.IsAuthenticated == true
                && repository?.Kind != RepositoryKind.Virtual
                && await CanManageAsync(caller, ownership, package.Id, cancellationToken))
            {
                manageUrl = RepositoryPageLinks.Manage(repository?.Name, package.Id);
            }

            screen = screen with { Owners = owners, OwnerAvatars = avatars, ManageUrl = manageUrl };
        }

        return ScreenResult<PackageScreen>.Ok(screen);
    }

    // The rule of the management page itself: an owner holding a management permission in this repository, or an administrator.
    private async Task<bool> CanManageAsync(ClaimsPrincipal user, PackageOwnershipService ownership, string? packageId, CancellationToken cancellationToken)
    {
        var userId = _services.GetService<UserManager<BaGetUser>>()?.GetUserId(user);
        if (await ownership.IsOwnerAsync(userId, user.Identity?.Name, packageId, cancellationToken)
            && await ManagePermission.HeldAnyAsync(_services, user, packageId, cancellationToken)) return true;

        var administrators = _services.GetService<AdministratorService>();
        return administrators != null && await administrators.IsAdministratorAsync(user, cancellationToken);
    }

    /// <summary>
    /// The license address, or null when there is none. A package that declares a license
    /// expression or file carries only the deprecation placeholder here, which leads nowhere useful.
    /// </summary>
    private static string? GetLicenseUrlOrNull(Package package)
    {
        var url = package.LicenseUrlString;
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith("https://aka.ms/deprecateLicenseUrl", StringComparison.OrdinalIgnoreCase)) return null;

        return url;
    }

    private static IReadOnlyList<PackageVersionEntry> ToVersions(IReadOnlyList<Package> packages, NuGetVersion selectedVersion)
        => packages
            .OrderByDescending(p => p.Version)
            .Select(p => new PackageVersionEntry(p.Version.ToNormalizedString(), p.Downloads, p.Version == selectedVersion, p.Published))
            .ToList();

    private async Task<string?> GetReadmeHtmlOrNullAsync(string? packageId, NuGetVersion packageVersion, CancellationToken cancellationToken)
    {
        string readme;
        using (var readmeStream = await _content.GetPackageReadmeStreamOrNullAsync(packageId, packageVersion, cancellationToken))
        {
            if (readmeStream == null) return null;

            using (var reader = new StreamReader(readmeStream))
            {
                readme = await reader.ReadToEndAsync();
            }
        }

        return ReadmeMarkdown.ToHtml(readme)?.Value;
    }
}
