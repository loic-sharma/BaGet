using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Extensions;

namespace BaGet.Tests.Support
{

    /// <summary>
    /// this is part of the NuGet Client simulation/mock. It ensures the same behavior of HttpClient like nuget.exe and dotnet restore
    /// </summary>
    public class NuGetLikeAuthenticationHandler : DelegatingHandler 
    {
        public NetworkCredential Credential { get; set; }

        public NuGetLikeAuthenticationHandler(HttpMessageHandler innerHandler) : base(innerHandler)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = null;
            bool isFirstLoop = true;
            request.Headers.Add("X-NuGet-Session-Id", "9999999"); //add before the loop (only ONE add-call)
            while (true)
            {
                // Clean up any previous responses
                if (response != null)
                {
                    response.Dispose();
                }
                
                response = await base.SendAsync(request, cancellationToken);

                if ((Credential == null) || (isFirstLoop == false))
                {
                    return response;
                }
                isFirstLoop = false;

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    request.Headers.Authorization = BasicAuthenticationHeaderBuilder.CreateWith(Credential);
                    continue; //second try
                }
                return response;
            } //while
        }
    }
}
