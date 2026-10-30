using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using BaGet.Identity.Application.Security;
using BaGet.Identity.Application.Services.Accounts;
using BaGet.Identity.Application.Services.Ownership;
using BaGet.Identity.Application.UseCases.Account;

namespace BaGet.Identity.Application.UseCases.Packages;

/// <summary>
/// The services every package-management use case (<c>Packages/*</c> screens) shares: the signed-in user's account, the sensitive-action
/// check, the session principal the format services authorize, the read-only switch. Registered scoped with the account services.
/// </summary>
public sealed class PackageScreenServices
{
    /// <summary>What every write answers on a read-only server.</summary>
    public const string ReadOnlyMessage = "This server is in read-only mode, so nothing can be changed.";

    private readonly ExternalLogins _logins;
    private readonly IOptionsSnapshot<BaGetOptions> _options;
    private readonly ISessionPrincipalFactory _sessions;
    private readonly IReauthenticationState _reauthentication;

    /// <summary>Creates the services.</summary>
    public PackageScreenServices(
        UserManager<BaGetUser> users,
        PackageOwnershipService ownership,
        AdministratorService administrators,
        IOptions<AccountsOptions> accounts,
        IOptionsSnapshot<BaGetOptions> options,
        ISessionPrincipalFactory sessions,
        IReauthenticationState reauthentication,
        IRepositoryContext repository,
        IServiceProvider services)
        : this(users, ownership, administrators, accounts, options, sessions, reauthentication, repository, services, ExternalLogins.For(users, accounts))
    {
    }

    /// <summary>Creates the services.</summary>
    public PackageScreenServices(
        UserManager<BaGetUser> users,
        PackageOwnershipService ownership,
        AdministratorService administrators,
        IOptions<AccountsOptions> accounts,
        IOptionsSnapshot<BaGetOptions> options,
        ISessionPrincipalFactory sessions,
        IReauthenticationState reauthentication,
        IRepositoryContext repository,
        IServiceProvider services,
        ExternalLogins logins)
    {
        _ = accounts ?? throw new ArgumentNullException(nameof(accounts));
        Users = users ?? throw new ArgumentNullException(nameof(users));
        Ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        Administrators = administrators ?? throw new ArgumentNullException(nameof(administrators));
        _logins = logins ?? throw new ArgumentNullException(nameof(logins));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _reauthentication = reauthentication ?? throw new ArgumentNullException(nameof(reauthentication));
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>Users, passwords.</summary>
    public UserManager<BaGetUser> Users { get; }

    /// <summary>Package and organization ownership.</summary>
    public PackageOwnershipService Ownership { get; }

    /// <summary>Server administrators.</summary>
    public AdministratorService Administrators { get; }

    /// <summary>The request's repository.</summary>
    public IRepositoryContext Repository { get; }

    /// <summary>The request's services, for the helpers that take a provider (<see cref="ManagePermission"/>, <see cref="ScanAccess"/>, the maintenance gate).</summary>
    public IServiceProvider Services { get; }

    /// <summary>The server's options.</summary>
    public BaGetOptions Options => _options.Value;

    /// <summary>Whether the server refuses writes.</summary>
    public bool IsReadOnlyMode => _options.Value.IsReadOnlyMode;

    /// <summary>Whether an external provider is available to sign in again with.</summary>
    public bool CanReauthenticate => _logins.Configured.Count > 0;

    // Whether a particular caller can is ConfirmationAsync(...).CanReauthenticate (a configured provider linked to the account).

    /// <summary>
    /// Whether the caller may see the package's pages in the request's repository (Read on the repository and the package). A refusal of the
    /// owner check answers 404 rather than 403 when this is false, so a private repository is not told from a missing one; the global
    /// repository filter would say the same, but the controller's own check runs before it.
    /// </summary>
    public async Task<bool> CanSeeAsync(ClaimsPrincipal caller, string? packageId, CancellationToken cancellationToken)
    {
        var access = (IRepositoryAccess?)Services.GetService(typeof(IRepositoryAccess));
        if (access == null) return true;

        var repository = Repository.Current;
        return string.IsNullOrEmpty(packageId)
            ? await access.CanReadAsync(caller, repository, cancellationToken).ConfigureAwait(false)
            : await access.CanReadPackageAsync(caller, repository, packageId!, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The session's principal for the format services (null when it cannot be built).</summary>
    public async Task<BaGetPrincipal?> CallerAsync(ClaimsPrincipal caller)
        => await _sessions.CreateAsync(caller).ConfigureAwait(false);

    /// <summary>
    /// The sensitive-action check: an account with a password types it again; one without (external sign-in only) must have signed in
    /// again with its provider a moment ago.
    /// </summary>
    public async Task<bool> IsConfirmedAsync(BaGetUser? user, string? password, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8604
        return await Users.HasPasswordAsync(user).ConfigureAwait(false)
            ? await Users.CheckPasswordAsync(user, password ?? string.Empty).ConfigureAwait(false)
            : await _reauthentication.HasReauthenticatedRecentlyAsync(cancellationToken).ConfigureAwait(false);
        #pragma warning restore CS8604
    }

    /// <summary>How the caller can confirm a sensitive action.</summary>
    public async Task<ConfirmationState> ConfirmationAsync(ClaimsPrincipal caller, CancellationToken cancellationToken)
    {
        var current = await Users.GetUserAsync(caller).ConfigureAwait(false);
        var hasPassword = current == null || await Users.HasPasswordAsync(current).ConfigureAwait(false);
        var reauthenticated = !hasPassword && await _reauthentication.HasReauthenticatedRecentlyAsync(cancellationToken).ConfigureAwait(false);

        return new ConfirmationState(hasPassword, reauthenticated, await _logins.CanReauthenticateAsync(current).ConfigureAwait(false));
    }

    /// <summary>The message of an owner change, shared by the management screens (<paramref name="unit"/> is "package", "artifact" …).</summary>
    public static string Describe(OwnerChangeResult result, string unit) => result switch
    {
        OwnerChangeResult.UnknownAccount => "There is no such user or organization.",
        OwnerChangeResult.AlreadyOwner => $"That account already owns the {unit}.",
        OwnerChangeResult.NotOwner => "That account is not an owner.",
        OwnerChangeResult.LastOwner => $"{(unit == "package" ? "A" : "An")} {unit} keeps at least one owner.",
        _ => result.ToString(),
    };
}
