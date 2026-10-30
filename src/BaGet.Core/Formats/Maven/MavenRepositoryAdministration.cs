using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Core;

/// <summary>Changes the settings of a local Maven repository (the administration page).</summary>
public interface IMavenRepositoryAdministration
{
    /// <summary>The settings of the Maven repository <paramref name="name"/>, or null when there is none of that name or format.</summary>
    Task<MavenRepositorySettings?> GetSettingsAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Saves <paramref name="settings"/> for the Maven repository <paramref name="name"/> and audits it. A stricter policy does not
    /// touch what is stored: it applies to later deploys.
    /// </summary>
    /// <returns>False when there is no Maven repository of that name.</returns>
    /// <exception cref="MaintenanceException">The server is read-only while a database copy runs.</exception>
    Task<bool> SaveSettingsAsync(string name, MavenRepositorySettings settings, string? actor, CancellationToken cancellationToken);
}

/// <summary><see cref="IMavenRepositoryAdministration"/> over the repositories table.</summary>
public class MavenRepositoryAdministration : IMavenRepositoryAdministration
{
    private readonly IContext _context;
    private readonly MaintenanceGate _gate;
    private readonly SystemTime _time;

    /// <param name="context">The database.</param>
    /// <param name="gate">The read-only gate.</param>
    /// <param name="time">The clock.</param>
    public MavenRepositoryAdministration(IContext context, MaintenanceGate gate, SystemTime time)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <inheritdoc/>
    public async Task<MavenRepositorySettings?> GetSettingsAsync(string name, CancellationToken cancellationToken)
    {
        var row = await _context.Repositories!.AsNoTracking().FirstOrDefaultAsync(r => r.Name == name && r.Format == RepositoryFormat.Maven, cancellationToken).ConfigureAwait(false);

        return row == null ? null : MavenRepositorySettings.Parse(row.Settings);
    }

    /// <inheritdoc/>
    public async Task<bool> SaveSettingsAsync(string name, MavenRepositorySettings settings, string? actor, CancellationToken cancellationToken)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        _gate.ThrowIfClosed();

        var row = await _context.Repositories!.FirstOrDefaultAsync(r => r.Name == name && r.Format == RepositoryFormat.Maven, cancellationToken).ConfigureAwait(false);
        if (row == null || row.Kind != RepositoryKind.Local) return false;

        row.Settings = settings.ToJson();
        AccessAdministration.Audit(_context, _time.UtcNow, actor, "repository.maven-settings", name, new { policy = settings.Policy.ToString(), settings.AllowRedeploy, settings.MaxSnapshotBuilds });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }
}
