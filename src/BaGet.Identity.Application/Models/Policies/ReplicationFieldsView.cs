using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>What the replication fields partial shows.</summary>
/// <param name="Form">The values.</param>
/// <param name="Repositories">The local repositories to choose from.</param>
/// <param name="IdPrefix">Prefix of the element ids, so two forms on one page do not clash.</param>
/// <param name="HasApiKey">Whether a key is stored (the field then says so).</param>
public sealed record ReplicationFieldsView(ReplicationForm Form, IReadOnlyList<RepositoryInfo> Repositories, string IdPrefix, bool HasApiKey);
