using BaGet.Core;

namespace BaGet.Identity.Application.Models.Access;

/// <summary>Adding a user to a group or removing them.</summary>
/// <param name="UserId">The user's id (<see cref="GroupMemberInfo.UserId"/>).</param>
/// <param name="Password">The administrator's password, asked again.</param>
public sealed record GroupMemberChange(string? UserId, string? Password);
