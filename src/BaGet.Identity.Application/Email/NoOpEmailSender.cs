using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace BaGet.Identity.Application.Email;

/// <summary>
/// Registered while email is not configured. Nothing that needs email is offered in that
/// case, so reaching this is a bug in the caller; it is logged instead of pretending to send.
/// </summary>
internal sealed class NoOpEmailSender : IEmailSender<BaGetUser>, IEmailMessageSender
{
    private readonly ILogger<NoOpEmailSender> _logger;

    public NoOpEmailSender(ILogger<NoOpEmailSender> logger) => _logger = logger;

    public Task SendConfirmationLinkAsync(BaGetUser user, string email, string confirmationLink) => Skip();

    public Task SendPasswordResetLinkAsync(BaGetUser user, string email, string resetLink) => Skip();

    public Task SendPasswordResetCodeAsync(BaGetUser user, string email, string resetCode) => Skip();

    public Task SendAsync(string to, string? replyTo, string? replyToName, string subject, string textBody, CancellationToken cancellationToken)
        => Skip();

    private Task Skip()
    {
        _logger.LogWarning("An email was requested but \"Email:Smtp\" is not configured; nothing was sent.");
        return Task.CompletedTask;
    }
}
