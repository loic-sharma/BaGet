using System.Text.Json.Serialization;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Access;

/// <summary>Giving a user or group a role in a project.</summary>
/// <param name="Type">User or group.</param>
/// <param name="PrincipalName">The principal's name.</param>
/// <param name="Role">The role.</param>
/// <param name="Password">The administrator's password, asked again.</param>
public sealed record ProjectMemberForm([property: JsonConverter(typeof(JsonStringEnumConverter))] PrincipalType Type, string? PrincipalName, [property: JsonConverter(typeof(JsonStringEnumConverter))] ProjectRole Role, string? Password);
