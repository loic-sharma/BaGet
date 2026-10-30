using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Web.V3.Models.Api;

/// <summary>A user as the UserDetail page shows them.</summary>
public sealed record UserDetailDto(
    UserDto User,
    IReadOnlyList<string> Groups,
    IReadOnlyList<UserProjectRole> ProjectRoles,
    IReadOnlyList<string> Organizations,
    int OwnedPackageIds,
    IReadOnlyList<ApiKeyDto> Keys);
