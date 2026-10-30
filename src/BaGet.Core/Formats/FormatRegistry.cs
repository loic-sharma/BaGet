using System;
using System.Collections.Generic;
using System.Linq;

namespace BaGet.Core;

/// <summary>
/// The registered <see cref="IFormatModule"/>s by format. Singleton; built once from the DI container, so a duplicate format or
/// a clash of storage folders fails at the first use rather than corrupting a store later.
/// </summary>
public class FormatRegistry
{
    /// <summary>The registry of a host that registered nothing: NuGet only (what tests and hand-built services use).</summary>
    public static FormatRegistry Default { get; } = new(new IFormatModule[] { new NuGetFormatModule() });

    private readonly Dictionary<RepositoryFormat, IFormatModule> _modules;

    /// <param name="modules">The modules; two for the same format, or claiming the same storage folder, are refused.</param>
    /// <exception cref="InvalidOperationException">A duplicate format or storage folder.</exception>
    public FormatRegistry(IEnumerable<IFormatModule> modules)
    {
        if (modules == null) throw new ArgumentNullException(nameof(modules));

        _modules = new Dictionary<RepositoryFormat, IFormatModule>();
        var folders = new Dictionary<string, RepositoryFormat>(StringComparer.OrdinalIgnoreCase)
        {
            [RepositoryStorage.ReposFolder] = default,
        };
        var prefixes = new Dictionary<string, RepositoryFormat>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules)
        {
            if (!_modules.TryAdd(module.Format, module))
            {
                throw new InvalidOperationException($"The {module.Format} format has two modules: {_modules[module.Format].GetType().Name} and {module.GetType().Name}.");
            }

            foreach (var folder in module.StorageFolders)
            {
                if (folders.TryGetValue(folder, out var owner))
                {
                    throw new InvalidOperationException(folder == RepositoryStorage.ReposFolder
                        ? $"The {module.Format} module claims the storage folder '{folder}', which holds the non-default repositories."
                        : $"The {module.Format} and {owner} modules both claim the storage folder '{folder}'.");
                }

                folders[folder] = module.Format;
            }

            // Two formats under one path segment would answer each other's routes. An empty prefix (NuGet) may be held once.
            if (prefixes.TryGetValue(module.RoutePrefix, out var other))
            {
                throw new InvalidOperationException($"The {module.Format} and {other} modules both use the route prefix '{module.RoutePrefix}'.");
            }

            prefixes[module.RoutePrefix] = module.Format;
        }
    }

    /// <summary>The modules, NuGet first, then by format.</summary>
    public IReadOnlyList<IFormatModule> Modules => _modules.Values.OrderBy(m => m.Format).ToList();

    /// <summary>The module of <paramref name="format"/>, or null when none is registered.</summary>
    public IFormatModule? Find(RepositoryFormat format) => _modules.TryGetValue(format, out var module) ? module : null;

    /// <summary>The module of <paramref name="format"/>.</summary>
    /// <exception cref="NotSupportedException">None is registered.</exception>
    public IFormatModule Require(RepositoryFormat format)
        => Find(format) ?? throw new NotSupportedException($"The {format} format is not available on this server.");
}
