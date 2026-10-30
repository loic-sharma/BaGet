using System.Collections.Generic;

namespace BaGet.Web.V3.Models.Api;

/// <summary>A virtual repository's members.</summary>
/// <param name="Members">The direct members in resolution order.</param>
/// <param name="DeployRepository">The member that receives deployments, if any.</param>
/// <param name="DeployTarget">The local repository a deployment really reaches, if any.</param>
/// <param name="EffectiveMembers">The local and remote repositories that answer, in order, nested virtual members expanded.</param>
public sealed record VirtualDto(IReadOnlyList<VirtualMemberDto> Members, string? DeployRepository, string? DeployTarget, IReadOnlyList<string> EffectiveMembers);
