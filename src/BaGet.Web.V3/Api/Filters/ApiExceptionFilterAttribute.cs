using System;
using System.Collections.Generic;
using System.Globalization;
using BaGet.Core;
using BaGet.Web.V3.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BaGet.Web.V3.Api.Filters;

/// <summary>
/// Turns what the administration services throw into problem answers: an unknown thing is 404, input they refuse is 400, a state
/// that forbids the change is 409, a closed <see cref="MaintenanceGate"/> is 503 with <c>Retry-After</c>. Anything else is a
/// server error and propagates.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ApiExceptionFilterAttribute : Attribute, IExceptionFilter
{
    /// <inheritdoc />
    public void OnException(ExceptionContext context)
    {
        var result = Map(context.Exception, context.HttpContext);
        if (result == null) return;

        context.Result = result;
        context.ExceptionHandled = true;
    }

    internal static IActionResult? Map(Exception exception, HttpContext http)
    {
        Exception? e = exception;
        for (var depth = 0; depth < 8 && e != null; depth++, e = e.InnerException)
        {
            if (e is MaintenanceException maintenance)
            {
                http.Response.Headers["Retry-After"] = MaintenanceGate.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);

                return ApiProblems.Create(503, "read-only", "The server is read-only", $"The server is read-only for maintenance ({maintenance.Reason}). Try again later.");
            }
        }

        return exception switch
        {
            KeyNotFoundException => ApiProblems.Create(404, "not-found", "Not found", exception.Message),
            RepositoryExistsException => ApiProblems.Conflict(exception.Message),
            RepositoryProtectedException or RepositoryNotEmptyException or RepositoryReferencedException => ApiProblems.Conflict(exception.Message),
            AccessModelException => ApiProblems.BadRequest(exception.Message),
            ArgumentException => ApiProblems.BadRequest(exception.Message),
            NotSupportedException => ApiProblems.Create(422, "not-supported", "Not supported here", exception.Message),
            // A password that nothing can protect (no data protection on the host): the services say so with this type, as the pages show it.
            InvalidOperationException when exception.GetType() == typeof(InvalidOperationException) && exception.Message.Contains("protect", StringComparison.OrdinalIgnoreCase)
                => ApiProblems.Create(422, "not-supported", "Not supported here", exception.Message),
            _ => null,
        };
    }
}
