using System.Text.Json.Serialization;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Access;

/// <summary>Removing a principal's grant or project role.</summary>
/// <param name="Type">User, group or organization.</param>
/// <param name="PrincipalId">The principal's id.</param>
/// <param name="Password">The administrator's password, asked again.</param>
public sealed record PrincipalRemoval([property: JsonConverter(typeof(JsonStringEnumConverter))] PrincipalType Type, string? PrincipalId, string? Password);
