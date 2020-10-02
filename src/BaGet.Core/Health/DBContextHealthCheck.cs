using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core.Health
{
    public class DbContextHealthCheck : IHealthCheck
    {
        private IContext Context { get; set; }

        public DbContextHealthCheck(IContext dbContext)
        {
            Context = dbContext;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var canConnect = await Context.Database.CanConnectAsync(cancellationToken);
            if(canConnect)
            {
                return HealthCheckResult.Healthy("Ok, but I dont feel like it today.");
            }
            else
            {
                return HealthCheckResult.Unhealthy("Cannot connect to database.");
            }
        }
    }
}
