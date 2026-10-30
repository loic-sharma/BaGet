using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using BaGet.Web.V3.Api.Catalog;
using BaGet.Web.V3.Models.Api;
using BaGet.Web.V3.Api;
using BaGet.Web.V3.Controllers.Base;

namespace BaGet.Web.V3.Controllers.Api;

/// <summary>
/// The Operations page's operations (catalog rebuild, downloads import, Docker garbage collection, vulnerability scan…): list them
/// with their last runs, start one on a repository, read a run's status. An operation that deletes data
/// (<see cref="IAdminOperation.RequiresConfirmation"/>) asks for the owner's password as the page does. Status is kept in memory per node.
/// </summary>
[Route(ApiCatalog.RootTemplate + "/operations")]
public class OperationsController : AdminApiController
{
    /// <summary>Lists the available operations.</summary>
    [HttpGet]
    [ApiOperation("List the operations this server offers, the repositories each applies to and their last runs", "AdminOperations", "Index", Response = typeof(OperationDto))]
    public async Task<IActionResult> List()
    {
        var jobs = Service<AdminJobService>();
        var repositories = await Service<IRepositoryService>().ListAsync(Cancellation);
        var result = new List<OperationDto>();
        foreach (var operation in jobs.Operations.Where(o => o.IsAvailable(HttpContext.RequestServices)))
        {
            var applies = repositories.Where(operation.AppliesTo).ToList();
            result.Add(new OperationDto(
                operation.Name,
                operation.Title,
                operation.Description,
                operation.RequiresConfirmation,
                applies.Select(r => r.Name).ToList(),
                applies.Select(r => jobs.Get(operation.Name, r.Name)).Where(s => s != null).Select(s => s!).ToList()));
        }

        return Respond(result);
    }

    /// <summary>The last run of an operation on a repository.</summary>
    [HttpGet("{operation}/repositories/{repository}")]
    [ApiOperation("Show the last run of an operation on a repository", "AdminOperations", "Index", Response = typeof(AdminJobStatus))]
    public IActionResult Status(string operation, string repository)
        => Service<AdminJobService>().Get(operation, repository) is { } status
            ? Respond(status)
            : ApiProblems.NotFound($"run of '{operation}' on '{repository}' since the server started");

    /// <summary>Starts an operation.</summary>
    [HttpPost("{operation}/repositories/{repository}")]
    [ApiOperation("Start an operation on a repository (one that deletes data asks for the password)", "AdminOperations", "Index", Confirm = true, Response = typeof(AdminJobStatus), Status = 202)]
    public async Task<IActionResult> Start(string operation, string repository)
    {
        var jobs = Service<AdminJobService>();
        var info = await Service<IRepositoryService>().FindAsync(repository, Cancellation);
        var definition = jobs.Operations.FirstOrDefault(o => o.Name == operation);
        if (info == null || definition == null || !definition.IsAvailable(HttpContext.RequestServices) || !definition.AppliesTo(info))
        {
            return ApiProblems.NotFound($"operation '{operation}' available for repository '{repository}'");
        }

        ThrowIfClosed();
        if (definition.RequiresConfirmation && await RequireConfirmationAsync() is { } refusal) return refusal;

        if (!jobs.TryStart(definition.Name, info, Actor, out var problem)) return ApiProblems.Conflict(problem ?? "The operation was not started.");

        return Respond(jobs.Get(definition.Name, info.Name), 202, $"{ApiCatalog.Root}/operations/{definition.Name}/repositories/{Uri.EscapeDataString(info.Name)}");
    }
}
