using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BaGet.Frontend.Mvc.Attributes;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace BaGet.Frontend.Routing;

/// <summary>
/// Which controller actions are repository pages (<see cref="RepositoryPageAttribute"/>), read from the registered actions.
/// <see cref="RepositoryUrlHelper"/> asks it whether a link to <c>controller/action</c> must carry the request's <c>repo</c>.
/// </summary>
internal sealed class UiActionMap
{
    private readonly IActionDescriptorCollectionProvider _actions;
    private UiActionMapSnapshot? _snapshot;

    public UiActionMap(IActionDescriptorCollectionProvider actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>Whether the action <paramref name="controller"/>.<paramref name="action"/> is marked <see cref="RepositoryPageAttribute"/>.</summary>
    public bool IsRepositoryAction(string controller, string action)
        => Current().RepositoryActions.Contains(controller + "/" + action);

    private UiActionMapSnapshot Current()
    {
        var collection = _actions.ActionDescriptors;
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot != null && snapshot.Version == collection.Version) return snapshot;

        var repositoryActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in collection.Items.OfType<ControllerActionDescriptor>())
        {
            if (action.EndpointMetadata.OfType<RepositoryPageAttribute>().Any()) repositoryActions.Add(action.ControllerName + "/" + action.ActionName);
        }

        snapshot = new UiActionMapSnapshot(collection.Version, repositoryActions);
        Volatile.Write(ref _snapshot, snapshot);

        return snapshot;
    }
}
