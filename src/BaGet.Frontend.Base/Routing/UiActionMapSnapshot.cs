using System.Collections.Generic;

namespace BaGet.Frontend.Routing;

/// <summary>What <see cref="UiActionMap"/> knows about the controller actions at one version of the action descriptors.</summary>
internal sealed record UiActionMapSnapshot(int Version, HashSet<string> RepositoryActions);
