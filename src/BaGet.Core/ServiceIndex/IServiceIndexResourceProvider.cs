using System.Collections.Generic;
using BaGet.Protocol.Models;

namespace BaGet.Core;

/// <summary>
/// Adds resources to the service index, for features that live outside BaGet.Core.
/// </summary>
public interface IServiceIndexResourceProvider
{
    IEnumerable<ServiceIndexItem> GetResources();
}