namespace BaGet.Identity.Application.Models.Policies;

/// <summary>Creating, saving or testing a replication, or any write of it that asks the password.</summary>
/// <param name="Form">The settings (<c>Form.*</c> on the page). The peer's API key is write only: no answer carries it.</param>
/// <param name="Password">The administrator's password.</param>
public sealed record ReplicationBody(ReplicationForm? Form, string? Password);
