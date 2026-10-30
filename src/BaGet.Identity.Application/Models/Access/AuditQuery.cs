namespace BaGet.Identity.Application.Models.Access;

/// <summary>The audit log's filter.</summary>
/// <param name="Actor">The user name that made the change.</param>
/// <param name="Subject">Part of what the change was done to.</param>
/// <param name="From">First day, <c>yyyy-MM-dd</c>.</param>
/// <param name="To">Last day, <c>yyyy-MM-dd</c>.</param>
public sealed record AuditQuery(string? Actor, string? Subject, string? From, string? To);
