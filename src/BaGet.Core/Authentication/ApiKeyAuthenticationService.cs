using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace BaGet.Core;

public class ApiKeyAuthenticationService : IAuthenticationService
{
    private readonly byte[]? _apiKey;

    public ApiKeyAuthenticationService(IOptionsSnapshot<BaGetOptions> options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        _apiKey = string.IsNullOrEmpty(options.Value.ApiKey)
            ? null
            : Encoding.UTF8.GetBytes(options.Value.ApiKey);
    }

    public Task<bool> AuthenticateAsync(string? apiKey, CancellationToken cancellationToken)
        => Task.FromResult(Authenticate(apiKey));

    private bool Authenticate(string? apiKey)
    {
        // No authentication is necessary if there is no required API key.
        if (_apiKey == null) return true;
        if (string.IsNullOrEmpty(apiKey)) return false;

        // Compared in fixed time. An ordinary string comparison returns at the first
        // differing character, so the time it takes says how much of the key was right,
        // and a caller who can measure that can learn the key one character at a time.
        // Only the length still leaks, which is what every fixed-time comparison does.
        return CryptographicOperations.FixedTimeEquals(_apiKey, Encoding.UTF8.GetBytes(apiKey));
    }
}