using System.Collections.Generic;

namespace BaGet.Core.Configuration
{
    public class BasicCredential
    {
        public string UserName { get; set; }

        public string Password { get; set; }

        public string Domain { get; set; }

    }

    public class BasicAuthenticationOptions 
    {
        public List<BasicCredential>AllowedUsers { get; set; }
    }

}
