using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Extensions;

namespace BaGet.Tests.Support
{
    public class NuGetLikeAuthenticationHandler : DelegatingHandler //simulates the same behavaior like a class with the same name inside NuGet.sln
    {
        public NetworkCredential AllowedUser { get; set; }

        public NuGetLikeAuthenticationHandler(HttpMessageHandler innerHandler) : base(innerHandler)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = null;
            bool isFirstLoop = true;
            while (true)
            {
                // Clean up any previous responses
                if (response != null)
                {
                    response.Dispose();
                }

                response = await base.SendAsync(request, cancellationToken);

                if ((AllowedUser == null) || (isFirstLoop == false))
                {
                    return response;
                }
                isFirstLoop = false;

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    request.Headers.Authorization = BasicAuthenticationHeaderBuilder.CreateWith(AllowedUser);
                    continue; //second try
                }
                return response;
            } //while
        }
    }
}
