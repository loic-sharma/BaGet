using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

/// <summary>
/// Validates BaGet's options, used at startup.
/// </summary>
public class ValidateStartupOptions
{
    private readonly IOptions<BaGetOptions> _root;
    private readonly IOptions<DatabaseOptions> _database;
    private readonly IOptions<StorageOptions> _storage;
    private readonly IOptions<MirrorOptions> _mirror;
    private readonly IOptions<SiteOptions> _site;
    private readonly ILogger<ValidateStartupOptions> _logger;
    private readonly IOptions<HaOptions>? _ha;
    private readonly HaBackendRules? _haRules;

    public ValidateStartupOptions(
        IOptions<BaGetOptions> root,
        IOptions<DatabaseOptions> database,
        IOptions<StorageOptions> storage,
        IOptions<MirrorOptions> mirror,
        IOptions<SiteOptions> site,
        ILogger<ValidateStartupOptions> logger,
        IOptions<HaOptions>? ha = null,
        HaBackendRules? haRules = null)
    {
        _ha = ha;
        _haRules = haRules;
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        _site = site ?? throw new ArgumentNullException(nameof(site));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool Validate()
    {
        try
        {
            // Access each option to force validations to run.
            // Invalid options will trigger an "OptionsValidationException" exception.
            _ = _root.Value;
            _ = _database.Value;
            _ = _storage.Value;
            _ = _mirror.Value;
            _ = _site.Value;

            // A cluster node on a local database or storage would diverge from the others: refuse before anything starts.
            if (_ha != null && _haRules != null)
            {
                var problems = _haRules.Check(_ha.Value, _database.Value, _storage.Value);
                foreach (var problem in problems) _logger.LogError("High availability: {Problem}", problem);
                if (problems.Count > 0) return false;
            }

            return true;
        }
        catch (OptionsValidationException e)
        {
            foreach (var failure in e.Failures)
            {
                _logger.LogError("{OptionsFailure}", failure);
            }

            _logger.LogError(e, "BaGet Extension configuration is invalid.");
            return false;
        }
    }
}