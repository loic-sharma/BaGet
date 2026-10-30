using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BaGet.Identity.Application.Models.Packages;
using BaGet.Identity.Application.RateLimiting;

namespace BaGet.Identity.Application.UseCases.Packages;

/// <summary>
/// Lets a signed-in user write to the owners of a package without learning their addresses. The message goes to every owning user and to the
/// administrators of every owning organization, one email each, with the sender in Reply-To. The screen needs accounts and mail (the caller's
/// gate); the sender needs a confirmed address, and is limited to one message per package per <see cref="ContactRateLimiter.Window"/>.
/// </summary>
public sealed class ContactOwnersUseCase
{
    private readonly PackageScreenServices _services;
    private readonly IContext _context;
    private readonly ContactRateLimiter _limiter;
    private readonly IEmailMessageSender _mail;
    private readonly ILogger<ContactOwnersUseCase> _logger;

    /// <summary>Creates the use case.</summary>
    public ContactOwnersUseCase(
        PackageScreenServices services,
        IContext context,
        ContactRateLimiter limiter,
        IEmailMessageSender mail,
        ILogger<ContactOwnersUseCase> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        _mail = mail ?? throw new ArgumentNullException(nameof(mail));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The GET handler: the form, 404 when the package has no owner.</summary>
    public async Task<ScreenResult<ContactOwnersViewModel>> GetAsync(ClaimsPrincipal caller, string? id, CancellationToken cancellationToken)
    {
        var model = new ContactOwnersViewModel();

        return await LoadAsync(caller, id, model, cancellationToken).ConfigureAwait(false) == null
            ? ScreenResult<ContactOwnersViewModel>.NotFound()
            : ScreenResult<ContactOwnersViewModel>.Ok(model);
    }

    /// <summary>The POST handler: validates, finds the recipients, rate limits and sends.</summary>
    /// <param name="caller">The signed-in user.</param>
    /// <param name="id">The package ID from the address.</param>
    /// <param name="message">The message to send.</param>
    /// <param name="copyToSelf">Whether the sender also gets a copy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<ScreenResult<ContactOwnersViewModel>> SendAsync(ClaimsPrincipal caller, string? id, string? message, bool copyToSelf, CancellationToken cancellationToken)
    {
        var model = new ContactOwnersViewModel { Message = message, CopyToSelf = copyToSelf };

        var owners = await LoadAsync(caller, id, model, cancellationToken).ConfigureAwait(false);
        if (owners == null) return ScreenResult<ContactOwnersViewModel>.NotFound();

        var sender = await _services.Users.GetUserAsync(caller).ConfigureAwait(false);
        if (sender == null) return ScreenResult<ContactOwnersViewModel>.SignIn();

        if (!model.SenderHasEmail)
        {
            model.Error = "Add an email address to your account and confirm it first, so the owners can reply.";
            return ScreenResult<ContactOwnersViewModel>.Ok(model);
        }

        var errors = ScreenValidation.Validate(model);
        if (errors.Count > 0) return ScreenResult<ContactOwnersViewModel>.Invalid(model, errors);

        var recipients = await FindRecipientsAsync(owners, cancellationToken).ConfigureAwait(false);
        if (recipients.Count == 0)
        {
            model.Error = "None of the owners can be reached by email.";
            return ScreenResult<ContactOwnersViewModel>.Ok(model);
        }

        if (!_limiter.TryAcquire(sender.Id, model.PackageId))
        {
            model.Error = "You can send one message per package every "
                          + (int)ContactRateLimiter.Window.TotalMinutes
                          + " minutes. Please try again later.";
            return ScreenResult<ContactOwnersViewModel>.Ok(model);
        }

        var subject = $"Message about your package {model.PackageId}";
        var body = BuildBody(sender, model.PackageId, model.Message!.Trim(), copy: false);
        var delivered = 0;

        foreach (var recipient in recipients)
        {
            try
            {
                #pragma warning disable CS8602
                await _mail.SendAsync(recipient, sender.Email.Trim(), sender.UserName, subject, body, cancellationToken).ConfigureAwait(false);
                #pragma warning restore CS8602
                delivered++;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not send a contact-owners message for {PackageId}.", model.PackageId);
            }
        }

        if (delivered == 0)
        {
            _limiter.Release(sender.Id, model.PackageId);
            model.Error = "The message could not be sent. Please try again later.";
            return ScreenResult<ContactOwnersViewModel>.Ok(model);
        }

        if (model.CopyToSelf)
        {
            try
            {
                #pragma warning disable CS8602
                await _mail.SendAsync(
                    sender.Email.Trim(),
                    replyTo: null,
                    replyToName: null,
                    subject: $"Copy of your message about {model.PackageId}",
                    textBody: BuildBody(sender, model.PackageId, model.Message!.Trim(), copy: true),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                #pragma warning restore CS8602
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not send the sender's copy of a contact-owners message for {PackageId}.", model.PackageId);
            }
        }

        model.Sent = true;
        return ScreenResult<ContactOwnersViewModel>.Ok(model);
    }

    private async Task<IReadOnlyList<PackageOwner>?> LoadAsync(ClaimsPrincipal caller, string? id, ContactOwnersViewModel model, CancellationToken cancellationToken)
    {
        var owners = await _services.Ownership.GetOwnersAsync(id, cancellationToken).ConfigureAwait(false);
        if (owners.Count == 0) return null;

        model.PackageId = owners[0].PackageId;

        var user = await _services.Users.GetUserAsync(caller).ConfigureAwait(false);
        model.SenderHasEmail = user != null && user.EmailConfirmed && IsDeliverable(user.Email);

        return owners;
    }

    /// <summary>The confirmed addresses of the user owners and of the administrators of the owning organizations, each once.</summary>
    private async Task<IReadOnlyList<string>> FindRecipientsAsync(IReadOnlyList<PackageOwner> owners, CancellationToken cancellationToken)
    {
        #pragma warning disable CS8602
        var names = owners.Select(o => o.OwnerName.ToLowerInvariant()).ToList();
        #pragma warning restore CS8602

        #pragma warning disable CS8602
        var userIds = await _services.Users.Users
            .AsNoTracking()
            .Where(u => names.Contains(u.UserName.ToLower()))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        #pragma warning restore CS8602

        #pragma warning disable CS8602, CS8604
        var adminIds = await _context.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.IsAdmin && m.Organization != null && names.Contains(m.Organization.Name.ToLower()))
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        #pragma warning restore CS8602, CS8604

        var ids = userIds.Concat(adminIds).Distinct().ToList();
        var emails = await _services.Users.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.EmailConfirmed)
            .Select(u => u.Email)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        #pragma warning disable CS8602
        return emails
            .Where(IsDeliverable)
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        #pragma warning restore CS8602
    }

    private static bool IsDeliverable(string? email)
        => !string.IsNullOrWhiteSpace(email) && new EmailAddressAttribute().IsValid(email.Trim());

    private static string BuildBody(BaGetUser sender, string? packageId, string message, bool copy)
    {
        var text = new StringBuilder();
        if (copy)
        {
            text.Append("This is a copy of the message you sent to the owners of ").Append(packageId).Append(".\n\n");
        }
        else
        {
            text.Append(sender.UserName).Append(" sent you this message about the package ").Append(packageId)
                .Append(". Reply to this email to answer them.\n\n");
        }

        text.Append("----\n").Append(message.Replace("\r\n", "\n").Replace('\r', '\n')).Append("\n----\n");
        if (!copy)
        {
            text.Append("\nYou received this because you own ").Append(packageId).Append(". Your address was not shown to the sender.\n");
        }

        return text.ToString();
    }
}
