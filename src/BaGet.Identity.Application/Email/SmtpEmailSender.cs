using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BaGet.Identity.Application.Email;

/// <summary>
/// Sends Identity's emails over SMTP with MailKit. A failure is logged and then
/// rethrown, so a caller can say "could not send" and never reports success.
/// </summary>
internal sealed class SmtpEmailSender : IEmailSender<BaGetUser>, IEmailMessageSender
{
    private readonly IOptions<EmailOptions> _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task SendConfirmationLinkAsync(BaGetUser user, string email, string confirmationLink)
        => SendAsync(email, "Confirm your email", LinkBody("Confirm your email address:", confirmationLink));

    public Task SendPasswordResetLinkAsync(BaGetUser user, string email, string resetLink)
        => SendAsync(email, "Reset your password", LinkBody("Reset your password:", resetLink));

    public Task SendPasswordResetCodeAsync(BaGetUser user, string email, string resetCode)
        => SendAsync(email, "Reset your password", $"<p>Your password reset code is <b>{WebUtility.HtmlEncode(resetCode)}</b>.</p>");

    /// <summary>
    /// A user's message as plain text. The subject is fixed text chosen by the caller, control
    /// characters are dropped from it and from the name, and addresses are parsed and encoded
    /// by MimeKit, which keeps line breaks out of headers.
    /// </summary>
    public Task SendAsync(
        string to,
        string? replyTo,
        string? replyToName,
        string subject,
        string textBody,
        CancellationToken cancellationToken)
        => SendAsync(BuildContactMessage(to, replyTo, replyToName, subject, textBody), subject, cancellationToken);

    internal MimeMessage BuildContactMessage(string to, string? replyTo, string? replyToName, string? subject, string textBody)
    {
        var message = new MimeMessage();
        #pragma warning disable CS8604
        message.From.Add(MailboxAddress.Parse(_options.Value.Smtp.From));
        #pragma warning restore CS8604
        message.To.Add(MailboxAddress.Parse(to));
        if (!string.IsNullOrEmpty(replyTo))
        {
            message.ReplyTo.Add(new MailboxAddress(StripControl(replyToName), replyTo));
        }

        #pragma warning disable CS8601
        message.Subject = StripControl(subject);
        #pragma warning restore CS8601
        message.Body = new TextPart("plain") { Text = textBody };

        return message;
    }

    private static string? StripControl(string? value)
        => value == null ? null : new string(value.Where(c => !char.IsControl(c)).ToArray());

    internal static string LinkBody(string text, string link)
    {
        var encoded = WebUtility.HtmlEncode(link);

        return $"<p>{WebUtility.HtmlEncode(text)}</p><p><a href=\"{encoded}\">{encoded}</a></p>";
    }

    internal MimeMessage BuildMessage(string to, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        #pragma warning disable CS8604
        message.From.Add(MailboxAddress.Parse(_options.Value.Smtp.From));
        #pragma warning restore CS8604
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        return message;
    }

    private Task SendAsync(string to, string subject, string htmlBody)
        => SendAsync(BuildMessage(to, subject, htmlBody), subject, CancellationToken.None);

    private async Task SendAsync(MimeMessage message, string subject, CancellationToken cancellationToken)
    {
        var smtp = _options.Value.Smtp;

        try
        {

            using var client = new SmtpClient();
            var security = !smtp.EnableSsl ? SecureSocketOptions.None
                : smtp.Port == 465 ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

            #pragma warning disable CS8604
            await client.ConnectAsync(smtp.Host, smtp.Port, security, cancellationToken);
            #pragma warning restore CS8604
            if (!string.IsNullOrEmpty(smtp.UserName))
            {
                #pragma warning disable CS8604
                await client.AuthenticateAsync(smtp.UserName, smtp.Password, cancellationToken);
                #pragma warning restore CS8604
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not send the email \"{Subject}\" through {Host}:{Port}.", subject, smtp.Host, smtp.Port);
            throw;
        }
    }
}
