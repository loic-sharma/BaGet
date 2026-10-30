using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// Sends a plain-text message that a user wrote, to one recipient. Unlike Identity's
/// own emails the content is untrusted, so implementations must not let it reach a header.
/// Only usable while <see cref="EmailOptions.IsConfigured"/>.
/// </summary>
public interface IEmailMessageSender
{
    /// <param name="to">The one recipient.</param>
    /// <param name="replyTo">Where a reply goes, or null.</param>
    /// <param name="replyToName">The reply-to's display name, or null.</param>
    /// <param name="subject">Fixed text chosen by the server.</param>
    /// <param name="textBody">The message.</param>
    /// <param name="cancellationToken">Cancels sending.</param>
    Task SendAsync(
        string to,
        string? replyTo,
        string? replyToName,
        string subject,
        string textBody,
        CancellationToken cancellationToken);
}
