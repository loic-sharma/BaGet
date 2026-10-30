namespace BaGet.Identity.Application.Models.Policies;

/// <summary>Lifting a legal hold.</summary>
/// <param name="Key">The hold's number.</param>
/// <param name="Password">The administrator's password.</param>
public sealed record RetentionHoldRemoval(int Key, string? Password);
