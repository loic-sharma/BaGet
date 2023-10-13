namespace BaGet.Core;

public interface IAuthenticationService
{
    Task<bool> AuthenticateAsync(string apiKey, CancellationToken cancellationToken);
}