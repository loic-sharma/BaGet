using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Web.V3.Models.Api;

/// <summary>What a user may do in one repository, and why.</summary>
public sealed record UserAccessDto(string Repository, AccessAction Actions, string? Override, IReadOnlyList<AccessReason> Reasons);
