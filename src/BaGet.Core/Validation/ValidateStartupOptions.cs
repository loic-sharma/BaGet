using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BaGet.Core
{
    /// <summary>
    /// Validates BaGet's options, used at startup.
    /// </summary>
    public class ValidateStartupOptions
    {
        private readonly IOptions<BaGetOptions> _root;
        private readonly IOptions<DatabaseOptions> _database;
        private readonly IOptions<StorageOptions> _storage;
        private readonly IOptions<MirrorOptions> _mirror;
        private readonly ILogger<ValidateStartupOptions> _logger;

        public ValidateStartupOptions(
            IOptions<BaGetOptions> root,
            IOptions<DatabaseOptions> database,
            IOptions<StorageOptions> storage,
            IOptions<MirrorOptions> mirror,
            ILogger<ValidateStartupOptions> logger)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
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

                return true;
            }
            catch (OptionsValidationException e)
            {
                foreach (var failure in e.Failures)
                {
                    _logger.LogError("{OptionsFailure}", failure);
                }

                _logger.LogError(e, "BaGet configuration is invalid.");
                return false;
            }
        }
    }
}
