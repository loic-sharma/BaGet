using System;
using Microsoft.AspNetCore.Builder;
using BaGet.Web.V3.Middleware;

namespace BaGet.Web.V3.Extensions;

public static class IApplicationBuilderExtensions
{
    public static IApplicationBuilder UseOperationCancelledMiddleware(this IApplicationBuilder app)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));

        return app.UseMiddleware<OperationCancelledMiddleware>();
    }

    /// <summary>
    /// Map writes refused by a closed <see cref="BaGet.Core.MaintenanceGate"/> to HTTP 503 with <c>Retry-After</c>.
    /// </summary>
    public static IApplicationBuilder UseMaintenanceMiddleware(this IApplicationBuilder app)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));

        return app.UseMiddleware<MaintenanceMiddleware>();
    }

    /// <summary>
    /// Select the repository of <c>/r/{repo}/</c> requests (404 for an unknown one). Call after <c>UseRouting</c> and
    /// before authentication: see <see cref="RepositoryResolutionMiddleware"/>.
    /// </summary>
    public static IApplicationBuilder UseRepositoryResolution(this IApplicationBuilder app)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));

        return app.UseMiddleware<RepositoryResolutionMiddleware>();
    }
}
