using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BaGet.Core.Configuration;
using Microsoft.Extensions.Options;

namespace BaGet.Core.Services
{
    public class ApiKeyAuthenticationService : IAuthenticationService, IDisposable
    {
        private string _apiKey;
        private readonly string _apiKeyHash;
        private readonly SHA256 _sha256;

        public ApiKeyAuthenticationService(IOptionsSnapshot<BaGetOptions> options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!string.IsNullOrEmpty(options.Value.ApiKeyHash) &&
                !string.IsNullOrEmpty(options.Value.ApiKey))
            {
                throw new ArgumentException(nameof(options.Value.ApiKey), $"Both {nameof(options.Value.ApiKey)} and {nameof(options.Value.ApiKey)} are set.");
            }

            if (!string.IsNullOrEmpty(options.Value.ApiKey))
            {
                _apiKey = options.Value.ApiKey;
            }
            if (!string.IsNullOrEmpty(options.Value.ApiKeyHash))
            {
                _apiKeyHash = options.Value.ApiKeyHash.ToLowerInvariant();
                _sha256 = SHA256.Create();
            }
        }

        public Task<bool> AuthenticateAsync(string apiKey) => Task.FromResult(Authenticate(apiKey));

        private bool Authenticate(string apiKey)
        {
            // No authentication is necessary if there is no required API key.
            if (_apiKey == null && _apiKeyHash == null)
                return true;

            // An API key is required.
            if (string.IsNullOrEmpty(apiKey))
                return false;

            // Check against key.
            if (_apiKey != null && _apiKey == apiKey)
                return true;

            // Check against hash.
            if (_apiKeyHash != null)
            {
                string hash = ComputeSHA256Hash(apiKey);
                if (hash == _apiKeyHash)
                {
                    // Cache what is very likely the key.
                    _apiKey = apiKey;
                    return true;
                }
            }

            return false;
        }

        private string ComputeSHA256Hash(string input)
        {
            var bytes = _sha256.ComputeHash(Encoding.ASCII.GetBytes(input));

            return BitConverter
                .ToString(bytes)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        public void Dispose() => _sha256?.Dispose();
    }
}
