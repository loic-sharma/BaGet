using System;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>
/// Records an administrator's action for the audit trail. This is the seam the access-audit table (access control
/// plan, U2) replaces: until then <see cref="LoggingAdminAudit"/> writes a log line. Implementations must not throw.
/// </summary>
public interface IAdminAudit
{
    /// <param name="actor">The signed-in administrator's name, or null when not known.</param>
    /// <param name="action">A short verb phrase, e.g. <c>validation.release</c>.</param>
    /// <param name="subject">What it was done to, e.g. <c>repository default, Contoso.Lib 1.0.0</c>.</param>
    /// <param name="details">Extra context, or null.</param>
    void Record(string? actor, string action, string subject, string? details = null);
}

/// <summary>The default <see cref="IAdminAudit"/>: one Information log line per action.</summary>
public sealed class LoggingAdminAudit : IAdminAudit
{
    private readonly ILogger<LoggingAdminAudit> _logger;

    public LoggingAdminAudit(ILogger<LoggingAdminAudit> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void Record(string? actor, string action, string subject, string? details = null)
        => _logger.LogInformation("Audit: {Actor} {Action} {Subject} {Details}", actor ?? "(unknown)", action, subject, details);
}
