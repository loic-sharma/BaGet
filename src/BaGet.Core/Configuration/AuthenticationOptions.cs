namespace BaGet.Core.Configuration
{
    public class AuthenticationOptions
    {
        public AuthenticationType Type { get; set; }
    }

    public enum AuthenticationType
    {
        None,
        AzureActiveDirectory,
    }
}
