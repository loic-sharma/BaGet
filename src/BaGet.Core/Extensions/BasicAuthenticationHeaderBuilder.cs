using System.Net.Http.Headers;
using System.Net;
using BaGet.Core.Authentication;

namespace BaGet.Core.Extensions
{
    public class BasicAuthenticationHeaderBuilder
    {
        public static AuthenticationHeaderValue CreateWith(NetworkCredential credential)
        {
            return new System.Net.Http.Headers.AuthenticationHeaderValue(BasicAuthenticationDefaults.AuthenticationScheme, AuthenticationHeaderValueExtensions.GetBase64BasicAuthHeaderString(credential));
        }
    }
}
