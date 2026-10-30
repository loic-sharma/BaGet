using System;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BaGet.Web.V3.Middleware;

/// <summary>
/// Turns a <see cref="MaintenanceException"/> (a write refused while the <see cref="MaintenanceGate"/>
/// is closed) into HTTP 503 with a <c>Retry-After</c> header and a plain-text explanation, so clients
/// see "try again later" instead of a server error. Reads are never affected.
/// </summary>
public class MaintenanceMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<MaintenanceMiddleware> _logger;

    public MaintenanceMiddleware(RequestDelegate next, ILogger<MaintenanceMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception e) when (!context.Response.HasStarted && Find(e) != null)
        {
            var maintenance = Find(e)!;
            _logger.LogWarning("Write refused during maintenance: {Reason}", maintenance.Reason);

            context.Response.Clear();
            context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            context.Response.Headers["Retry-After"] = MaintenanceGate.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                $"The server is read-only for maintenance ({maintenance.Reason}). Try again later.");
        }
    }

    private static MaintenanceException? Find(Exception? e)
    {
        for (var depth = 0; e != null && depth < 8; depth++, e = e.InnerException)
        {
            if (e is MaintenanceException maintenance) return maintenance;
        }

        return null;
    }
}
