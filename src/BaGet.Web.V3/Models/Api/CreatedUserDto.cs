namespace BaGet.Web.V3.Models.Api;

/// <summary>The created account; <see cref="GeneratedPassword"/> is shown this once.</summary>
public sealed record CreatedUserDto(UserDto User, string? GeneratedPassword);
