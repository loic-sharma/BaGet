using BaGet.Core;

namespace BaGet.Web.Application.Services;

/// <summary>
/// Whether the server can take an upload at all, independent of who is asking. One rule for the Upload page and the
/// navigation that links to it, so the site never offers a page that could only explain why it is closed.
/// </summary>
internal static class UploadAvailability
{
    /// <summary>
    /// Nothing on the server can tell one caller from another: accounts are off and no global API key is set,
    /// which is how BaGet ships.
    /// </summary>
    internal static bool NothingCanAuthenticate(BaGetOptions options, AccountsOptions accounts)
        => !accounts.Enabled && string.IsNullOrEmpty(options.ApiKey);

    /// <summary>
    /// Whether an upload can succeed for someone able to authenticate: not read-only, the maintenance gate open
    /// (a database copy closes it) and something to authenticate with. Signing in is not required here: an
    /// anonymous visitor is sent to sign in by the page itself.
    /// </summary>
    internal static bool IsPossible(BaGetOptions options, AccountsOptions accounts, MaintenanceGate? gate)
        => !options.IsReadOnlyMode
            && !(gate?.IsClosed ?? false)
            && !NothingCanAuthenticate(options, accounts);
}
