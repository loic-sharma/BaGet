using System;
using BaGet.Core.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet.Extensions
{
    public static class AzureAdServiceCollectionExtensions
    {
        public static AuthenticationBuilder AddAzureAdBearer(this AuthenticationBuilder builder, Action<AzureAdOptions> configureOptions)
        {
            builder.Services.Configure(configureOptions);
            builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureAzureAdOptions>();
            builder.AddJwtBearer();
            return builder;
        }

        public static AuthenticationBuilder AddJwtBearer1(this AuthenticationBuilder builder, Action<BaGetJwtBearerOptions> configureOptions)
        {
            builder.Services.Configure(configureOptions);
            builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureBaGetJwtBearerOptions>();
            builder.AddJwtBearer();
            return builder;
        }
    }
}
