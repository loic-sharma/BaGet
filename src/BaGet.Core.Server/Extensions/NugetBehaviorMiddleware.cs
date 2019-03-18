using System;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace BaGet.Extensions
{
    /// <summary>
    /// Special middleware that translates the non RFC behavior from NuGet.exe into default Bearer RFC behavior before the default JwtBearer Middleware from the AspNetCore Team is called
    /// This special rewrite of request/response is executed only if the client is the nuget commandline. Other clients (Web-UI) are expected to be implemented RFC conform!
    /// </summary>
    public class NugetBehaviorMiddleware
    {
        private readonly RequestDelegate NextRequest;
        private readonly ILogger Logger;
        private const string BasicAuthenticationScheme = "Basic";

        public NugetBehaviorMiddleware(RequestDelegate next, ILogger<NugetBehaviorMiddleware> logger)
        {
            NextRequest = next ?? throw new ArgumentNullException(nameof(next));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private void ModifyRequest(HttpContext context, bool isNuGetClientCall)
        {

            if (!isNuGetClientCall) return;

            string authorization = context.Request.Headers[HeaderNames.Authorization];
            // If no authorization header found, nothing to process further
            if (string.IsNullOrEmpty(authorization))
            {
                return;
            }

            var authHeader = AuthenticationHeaderValue.Parse(authorization);
            if (string.IsNullOrEmpty(authHeader.Parameter)) return;

            if (authHeader.Scheme == BasicAuthenticationScheme)
            {
                var credentialBytes = Convert.FromBase64String(authHeader.Parameter);
                var credentialSplit = Encoding.UTF8.GetString(credentialBytes).Split(':');

                if (credentialSplit.Length == 0)
                {
                    return;
                }

                var username = credentialSplit[0];
                var password = string.Empty;

                if (credentialSplit.Length > 1)
                {
                    password = credentialSplit[1];
                }
                //NuGet.exe has only "Basic" implemented. convert this into "Bearer" directly on the request/response pipeline, then we can use the default jwt implementation from aspnetcore
                context.Request.Headers[HeaderNames.Authorization] = $"{Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme} {password}";
                Logger.LogTrace($"Request header field '{HeaderNames.Authorization}' rewritten to '{Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme}'");
            }
        }

        private void ModifyResponse(HttpContext context, bool isNuGetClientCall)
        {
            if (isNuGetClientCall)
            {
                if (context.Response.StatusCode == 401)
                {
                    context.Response.Headers[HeaderNames.WWWAuthenticate] = BasicAuthenticationScheme; //NuGet.exe supports "Basic" ONLY!
                    Logger.LogTrace($"Response header field '{HeaderNames.WWWAuthenticate}' rewritten to '{BasicAuthenticationScheme}'");
                }
            }
        }

        public async Task Invoke(HttpContext context)
        {
            var isNuGetClientCall = context.Request.Headers.ContainsKey("X-NuGet-Session-Id");
            Logger.LogTrace($"{nameof(isNuGetClientCall)}={isNuGetClientCall}");
            ModifyRequest(context, isNuGetClientCall);

            await NextRequest(context);

            ModifyResponse(context, isNuGetClientCall);
        }
    }
}
