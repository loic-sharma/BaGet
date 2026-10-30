using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BaGet.Identity.Application.Email;

/// <summary>
/// Mails a user a password reset link. The one implementation behind "forgot password" and the
/// administrator's "email link": a page builds the link template (see <see cref="Template"/>),
/// then this fills in the user and a fresh token off the request path.
/// </summary>
public class PasswordResetLinkSender
{
    /// <summary>Where the user's id goes in a link template.</summary>
    public const string UserIdPlaceholder = "USERIDPLACEHOLDER";

    /// <summary>Where the encoded token goes in a link template.</summary>
    public const string CodePlaceholder = "CODEPLACEHOLDER";

    private readonly UserManager<BaGetUser> _users;
    private readonly IEmailSender<BaGetUser> _sender;
    private readonly ILogger<PasswordResetLinkSender> _logger;

    /// <summary>Creates the sender.</summary>
    public PasswordResetLinkSender(UserManager<BaGetUser> users, IEmailSender<BaGetUser> sender, ILogger<PasswordResetLinkSender> logger)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The route values of the reset page's link, with the placeholders to fill in later.</summary>
    public static object Template => new { userId = UserIdPlaceholder, code = CodePlaceholder };

    /// <summary>
    /// Queue the mail to be sent off the request path. False when the queue is full.
    /// </summary>
    /// <param name="requestServices">The request's services.</param>
    /// <param name="userId">The user to mail.</param>
    /// <param name="template">The link built from <see cref="Template"/>.</param>
    public static bool TryEnqueue(IServiceProvider requestServices, string userId, string template)
        => requestServices.GetRequiredService<BackgroundEmailQueue>()
            .TryEnqueue(services => services.GetRequiredService<PasswordResetLinkSender>().SendAsync(userId, template));

    /// <summary>
    /// Mail the link. Does nothing for a user that no longer exists or has no address; a failure
    /// to send is logged, not thrown.
    /// </summary>
    public async Task SendAsync(string userId, string template, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user == null || string.IsNullOrEmpty(user.Email)) return;

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var link = template
            .Replace(UserIdPlaceholder, Uri.EscapeDataString(user.Id))
            .Replace(CodePlaceholder, EncodeToken(token));

        try
        {
            await _sender.SendPasswordResetLinkAsync(user, user.Email, link);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not send a password reset link.");
        }
    }

    /// <summary>Identity's tokens carry characters that do not survive a URL, so they travel encoded.</summary>
    internal static string EncodeToken(string token)
        => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
}
