using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BaGet.Core.Services
{
    public class ApiKeyAuthenticationService : IAuthenticationService, IDisposable
    {
        private readonly string _apiKeyHash;
        private readonly SHA256 _sha256;

        public ApiKeyAuthenticationService(string apiKeyHash)
        {
            if (string.IsNullOrEmpty(apiKeyHash))
            {
                apiKeyHash = null;
            }
            else
            {
                _apiKeyHash = apiKeyHash.ToLowerInvariant();
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
