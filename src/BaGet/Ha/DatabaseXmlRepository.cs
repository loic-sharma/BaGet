using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using BaGet.Core;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet.Ha;

/// <summary>
/// The ASP.NET Core data protection key ring stored in the <c>DataProtectionKeys</c> table, so every node of a cluster reads and
/// writes the same keys (sign-in cookies, antiforgery tokens, protected remote credentials and replication peer keys). Used only when
/// <c>Ha:Enabled</c>; without it the host keeps its own key ring. Two nodes that start on an empty table may each add a key: the
/// key manager honours all of them. A key is stored through a context exempt from the read-only gate
/// (<see cref="MaintenanceGate.AllowWrites(DbContextOptionsBuilder)"/>, opened through <see cref="IDatabaseContextFactory"/> like the
/// database copy's target): the gate is shared by the cluster, and a refused key would break sign-in and antiforgery for every node.
/// </summary>
internal sealed class DatabaseXmlRepository : IXmlRepository
{
    private readonly IServiceScopeFactory _scopes;

    public DatabaseXmlRepository(IServiceScopeFactory scopes) => _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = _scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return context.DataProtectionKeys!
            .AsNoTracking()
            .OrderBy(k => k.Key)
            .Select(k => k.Xml)
            .ToList()
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .Select(xml => XElement.Parse(xml!))
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var scope = _scopes.CreateScope();
        var name = friendlyName ?? string.Empty;
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>().CurrentValue;
        var factory = scope.ServiceProvider.GetServices<IDatabaseContextFactory>()
            .FirstOrDefault(f => string.Equals(f.DatabaseType, options.Type, StringComparison.OrdinalIgnoreCase));

        // A relational database (HA requires one) opens its own gate-exempt context; the application's context is the fallback.
        var exempt = factory != null && !string.IsNullOrEmpty(options.ConnectionString)
            ? factory.Create(options.ConnectionString, options, builder => MaintenanceGate.AllowWrites(builder))
            : null;
        try
        {
            Store(exempt ?? scope.ServiceProvider.GetRequiredService<IContext>(), element, name);
        }
        finally
        {
            if (exempt is IDisposable disposable) disposable.Dispose();
        }
    }

    private static void Store(IContext context, XElement element, string name)
    {
        context.DataProtectionKeys!.Add(new DataProtectionKeyEntry
        {
            FriendlyName = name.Length > DataProtectionKeyEntry.MaxNameLength ? name.Substring(0, DataProtectionKeyEntry.MaxNameLength) : name,
            Xml = element.ToString(SaveOptions.DisableFormatting),
        });
        context.SaveChangesAsync(default).GetAwaiter().GetResult();
    }
}
