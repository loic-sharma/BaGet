using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>What the policy fields partial shows.</summary>
/// <param name="Form">The values.</param>
/// <param name="Repositories">The repositories to choose from.</param>
/// <param name="Projects">The projects to choose from.</param>
/// <param name="IdPrefix">Prefix of the element ids, so two forms on one page do not clash.</param>
public sealed record RetentionFieldsView(RetentionPolicyForm Form, IReadOnlyList<RepositoryInfo> Repositories, IReadOnlyList<string> Projects, string IdPrefix);
