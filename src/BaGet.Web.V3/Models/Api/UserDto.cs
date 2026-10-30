using System;

namespace BaGet.Web.V3.Models.Api;

/// <summary>A user in a list.</summary>
public sealed record UserDto(string UserName, string? Email, DateTime Created, bool IsAdministrator, bool PendingApproval = false);
