using System;
using System.Threading.Tasks;
using BaGet.Core.Configuration;
using Microsoft.Extensions.Options;

namespace BaGet.Core.Services
{
    public class ApiKeyAuthenticationService : IAuthenticationService
    {
        private readonly string _apiKey;

        public ApiKeyAuthenticationService(IOptionsSnapshot<BaGetOptions> options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            _apiKey = string.IsNullOrEmpty(options.Value.ApiKey) ? null : options.Value.ApiKey;
        }

        public Task<bool> AuthenticateAsync(string apiKey) => Task.FromResult(Authenticate(apiKey));

        private bool Authenticate(string apiKey)
        {
            // No authentication is necessary if there is no required API key.
            if (_apiKey == null) return true;

            return _apiKey == apiKey;
        }
    }
}
