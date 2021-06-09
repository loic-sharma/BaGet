using System;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BaGet.Database.RavenDb
{
    public static class RavenApplicationExtensions
    {
        public static BaGetApplication AddRavenDatabase(this BaGetApplication app)
        {
            app.Services.AddBaGetGenericContextProvider<RavenContext>("RavenDb");
            return app;
        }

        public static BaGetApplication AddRavenDatabase(
            this BaGetApplication app,
            Action<DatabaseOptions> configure)
        {
            app.AddRavenDatabase();
            app.Services.Configure(configure);
            return app;
        }
    }
}
