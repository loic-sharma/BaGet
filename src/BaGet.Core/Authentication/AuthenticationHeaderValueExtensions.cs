using System;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace BaGet.Core.Authentication
{
    public class AuthenticationHeaderValueExtensions
    {
        public static string GetBase64BasicAuthHeaderString(NetworkCredential credential)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            return Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(GetBasicAuthHeaderString(credential)));
        }

        public static string GetBasicAuthHeaderString(NetworkCredential credential)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            var username = credential.UserName;
            if (string.IsNullOrEmpty(credential.Domain) == false)
            {
                username = $"{credential.UserName}@{credential.Domain}";
            }
            return $"{username}:{credential.Password}";
        }

        public static bool TryGetNetworkCredentialFromHeader(AuthenticationHeaderValue authHeader, out NetworkCredential credential)
        {
            if (authHeader == null) throw new ArgumentNullException(nameof(authHeader));
            credential = null;
            if (string.IsNullOrEmpty(authHeader.Parameter)) return false;
            var credentialBytes = Convert.FromBase64String(authHeader.Parameter);
            var credentialString = Encoding.UTF8.GetString(credentialBytes);
            return TryGetNetworkCredentialFromString(credentialString, out credential);
        }

        public static bool TryGetNetworkCredentialFromString(string credentialString, out NetworkCredential credential)
        {
            if (credentialString == null) throw new ArgumentNullException(nameof(credentialString));

            credential = null;
            var firstColon = credentialString.IndexOf(':');
            if (firstColon < 0) return false;
            var userPart = credentialString.Substring(0, firstColon);
            var password = credentialString.Substring(firstColon+1);
            var username = userPart;
            string domain = null;
            var firstAt = userPart.IndexOf('@');
            if (firstAt > -1)
            {
                domain = userPart.Substring(firstAt + 1);
                username = userPart.Substring(0, firstAt);
            }
            credential = new NetworkCredential(username, password, domain);
            return true;
        }
    }
}
