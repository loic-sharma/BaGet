using Microsoft.AspNetCore.Authentication;

namespace BaGet.Extensions
{
    public class BasicAuthenticationOptions : AuthenticationSchemeOptions
    {
        public string Realm { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public string Domain { get; set; }
    }
}
