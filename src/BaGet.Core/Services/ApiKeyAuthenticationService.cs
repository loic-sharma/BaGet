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
        private readonly string _apiKeyHash;
        private readonly SHA256 _sha256;

        public ApiKeyAuthenticationService(IOptionsSnapshot<BaGetOptions> options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            if (string.IsNullOrEmpty(options.Value.ApiKeyHash))
            {
                _apiKeyHash = null;
            }
            else
            {
                _apiKeyHash = options.Value.ApiKeyHash.ToLowerInvariant();
            }

            _sha256 = SHA256.Create();
        }

        public Task<bool> AuthenticateAsync(string apiKey) => Task.FromResult(Authenticate(apiKey));

        private bool Authenticate(string apiKey)
        {
            // No authentication is necessary if there is no required API key.
            if (_apiKeyHash == null) return true;

            // Otherwise, an API key is required.
            if (string.IsNullOrEmpty(apiKey)) return false;

            return _apiKeyHash == ComputeSHA256Hash(apiKey);
        }

        private string ComputeSHA256Hash(string input)
        {
            var bytes = _sha256.ComputeHash(Encoding.ASCII.GetBytes(input));

            return BitConverter
                .ToString(bytes)
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        public void Dispose() => _sha256.Dispose();
    }
}
