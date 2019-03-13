using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet.Extensions
{
    public static class BasicAuthenticationBuilderExtensions
    {
        public static AuthenticationBuilder AddBasic(this AuthenticationBuilder builder, string authenticationScheme, Action<BasicAuthenticationOptions> configureOptions)
        {
            if (configureOptions == null) throw new ArgumentNullException(nameof(configureOptions));

            var vs = new ValidatorService();
            System.Net.NetworkCredential allowedUser = null;
            builder.Services.AddSingleton<IPostConfigureOptions<BasicAuthenticationOptions>, BasicAuthenticationPostConfigureOptions>();
            builder.Services.AddSingleton<IBasicAuthenticationService>(vs);

            vs.Validator = (c) =>
            {
                if (allowedUser == null)
                {
                    return Task.FromResult(false);
                }

                return Task.FromResult((allowedUser.UserName == c.UserName) && allowedUser.Password == c.Password && allowedUser.Domain== c.Domain);

            };

            return builder.AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler>(
                authenticationScheme, (opt) =>
                {
                    configureOptions(opt);
                    allowedUser = new System.Net.NetworkCredential(opt.UserName, opt.Password, opt.Domain);
                });
        }
    }
}
