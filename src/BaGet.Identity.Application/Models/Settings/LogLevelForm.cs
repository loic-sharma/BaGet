namespace BaGet.Identity.Application.Models.Settings;

/// <summary>Setting or removing the lowest level logged for one source.</summary>
/// <param name="Source">The source (a namespace prefix).</param>
/// <param name="Level">The level (ignored when removing).</param>
/// <param name="Password">The administrator's password.</param>
public sealed record LogLevelForm(string? Source, string? Level, string? Password);
