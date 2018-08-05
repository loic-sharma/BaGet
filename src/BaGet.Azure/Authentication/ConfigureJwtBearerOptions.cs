using System;
using System.Text;
using System.Threading.Tasks;
using BaGet.Azure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace BaGet.Azure.Authentication
{
    public class ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
    {
        private readonly AzureActiveDirectoryOptions _options;

        public ConfigureJwtBearerOptions(IOptions<AzureActiveDirectoryOptions> options)
        {
            _options = options.Value;
        }

        public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);

        public void Configure(string name, JwtBearerOptions options)
        {
            options.Audience = _options.ClientId;
            options.Authority = $"{_options.Instance}{_options.TenantId}";

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (TryParseToken(context.HttpContext, out var token))
                    {
                        context.Token = token;
                    }

                    return Task.CompletedTask;
                }
            };
        }

        private bool TryParseToken(HttpContext context, out string token)
        {
            // Try to parse a JSON Web Token from the password of Basic Authorization.
            token = null;
            string authorization = context.Request.Headers["Authorization"];

            if (string.IsNullOrEmpty(authorization))
            {
                return false;
            }

            if (!authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var encodedCredentials = authorization.Substring("Basic ".Length).Trim();
            var crendentials = Encoding.ASCII.GetString(Convert.FromBase64String(encodedCredentials));

            if (!crendentials.StartsWith("BaGet:"))
            {
                return false;
            }

            token = crendentials.Substring("BaGet:".Length);
            return true;
        }
    }
}
