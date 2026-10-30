using System.Text.Json.Serialization;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Access;

/// <summary>Giving a principal the ticked actions through a permission target, replacing an earlier grant to it.</summary>
/// <param name="PrincipalType">User, group or organization.</param>
/// <param name="PrincipalName">The principal's name.</param>
/// <param name="Actions">The ticked <see cref="AccessAction"/> names (<c>Read</c>, <c>Annotate</c>, <c>Deploy</c>, <c>Delete</c>, <c>Manage</c>); none removes the grant.</param>
/// <param name="Password">The administrator's password, asked again.</param>
public sealed record GrantForm([property: JsonConverter(typeof(JsonStringEnumConverter))] PrincipalType? PrincipalType, string? PrincipalName, string[]? Actions, string? Password);
