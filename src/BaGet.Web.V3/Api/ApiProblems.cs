using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.V3.Models.Api;

namespace BaGet.Web.V3.Api;

/// <summary>Builds the RFC 9457 problem answers of the API (<c>application/problem+json</c>).</summary>
public static class ApiProblems
{
    /// <summary>Base of the problem <c>type</c> URIs.</summary>
    public const string TypeBase = "https://baget.dev/problems/";

    /// <summary>A problem result with the given status, short title, detail and a stable <paramref name="code"/> (the last part of <c>type</c>).</summary>
    public static ObjectResult Create(int status, string code, string title, string? detail = null, IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Type = TypeBase + code };
        problem.Extensions["code"] = code;
        if (extensions != null)
        {
            foreach (var pair in extensions) problem.Extensions[pair.Key] = pair.Value;
        }

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>404.</summary>
    public static ObjectResult NotFound(string what) => Create(404, "not-found", "Not found", $"There is no {what}.");

    /// <summary>400.</summary>
    public static ObjectResult BadRequest(string detail) => Create(400, "invalid-request", "Invalid request", detail);

    /// <summary>409.</summary>
    public static ObjectResult Conflict(string detail) => Create(409, "conflict", "Conflict", detail);

    /// <summary>The page number and size from the query, clamped.</summary>
    public static (int Page, int PageSize) Clamp(int? page, int? pageSize)
        => (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? Paged<int>.DefaultPageSize, 1, Paged<int>.MaxPageSize));
}
