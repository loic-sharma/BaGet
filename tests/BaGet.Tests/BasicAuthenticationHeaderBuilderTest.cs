using System.Net;
using BaGet.Core.Extensions;
using Xunit;
namespace BaGet.Tests
{
    public class BasicAuthenticationHeaderBuilderTest
    {
        [Theory]
        [InlineData("myUserName","myPassword", "myDomain")]
        [InlineData("myUserName", "myPassword", null)]
        [InlineData("myUserName", "myPassword", "")]
        [InlineData("myUserName", "myPassword", " ")]
        [InlineData("myUserName", "my:Pass:word", "xyz")]
        [InlineData("myUserName", "my@Pass@word", "xyz")]
        public void SerializeToHeaderAndDeserialize(string userName, string password, string domain)
        {
            var credential = new NetworkCredential(userName, password, domain);
            var header = BasicAuthenticationHeaderBuilder.CreateWith(credential);
            Assert.True(AuthenticationHeaderValueExtensions.TryGetNetworkCredentialFromHeader(header, out var cred2));
            Assert.Equal(credential.UserName, cred2.UserName);
            Assert.Equal(credential.Password, cred2.Password);
            Assert.Equal(credential.Domain, cred2.Domain);
        }
    }
}
