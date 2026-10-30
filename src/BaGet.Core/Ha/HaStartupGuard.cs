using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>
/// Refuses to start a node whose <c>Ha:Enabled</c> setting cannot work: a database or storage that is local to one machine, or no
/// Redis connection string (<see cref="HaBackendRules"/>). A cluster of nodes that each write their own SQLite file would silently
/// diverge, so the start fails with the reasons instead.
/// </summary>
public sealed class HaStartupGuard : IHostedService
{
    private readonly IOptions<HaOptions> _ha;
    private readonly IOptions<DatabaseOptions> _database;
    private readonly IOptions<StorageOptions> _storage;
    private readonly HaBackendRules _rules;
    private readonly ILogger<HaStartupGuard> _logger;

    /// <summary>Creates the guard.</summary>
    public HaStartupGuard(
        IOptions<HaOptions> ha,
        IOptions<DatabaseOptions> database,
        IOptions<StorageOptions> storage,
        HaBackendRules rules,
        ILogger<HaStartupGuard> logger)
    {
        _ha = ha ?? throw new ArgumentNullException(nameof(ha));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var problems = _rules.Check(_ha.Value, _database.Value, _storage.Value);
        if (problems.Count == 0)
        {
            if (_ha.Value.Enabled && string.IsNullOrWhiteSpace(_ha.Value.KeyCertificatePath))
            {
                _logger.LogWarning("High availability: the data protection keys are stored unencrypted in the database, next to the passwords and peer keys they protect. Set Ha:KeyCertificatePath (a PFX every node holds).");
            }

            return Task.CompletedTask;
        }

        foreach (var problem in problems) _logger.LogCritical("High availability: {Problem}", problem);

        throw new InvalidOperationException("Ha:Enabled cannot start: " + string.Join(" ", problems));
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
