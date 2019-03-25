using System.Net;
using System.Net.Http.Headers;

namespace BaGet.Core.Authentication
{
    public class BasicAuthenticationHeaderBuilder
    {
        public static AuthenticationHeaderValue CreateWith(NetworkCredential credential)
        {
            return new AuthenticationHeaderValue(BasicAuthenticationDefaults.AuthenticationScheme, credential.GetBase64BasicAuthHeaderString());
        }
    }
}
