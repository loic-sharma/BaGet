using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.UseCases.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Frontend.Controllers.Base;

/// <summary>
/// Base of the retention screens (policies, a policy's report, legal holds). The access screens' rules (administrators only, 404 for anyone else, redirect where there is no
/// relational database, 503 on a POST while the server is read-only), with the retention services and the lists the forms choose
/// from. A <see cref="RetentionException"/> is an <see cref="AccessModelException"/>, so <c>ActAsync</c> turns it into the screen's
/// <c>Error</c>.
/// </summary>
public abstract class RetentionControllerBase : AccessControllerBase
{
    /// <summary>Policies and legal holds.</summary>
    protected IRetentionAdministration Retention => HttpContext.RequestServices.GetRequiredService<IRetentionAdministration>();

    /// <summary>Runs policies in the background and keeps their last report.</summary>
    protected RetentionJobs Jobs => HttpContext.RequestServices.GetRequiredService<RetentionJobs>();

    /// <summary>The repositories a policy or hold can name (virtual ones store nothing), by name.</summary>
    public IReadOnlyList<RepositoryInfo> Repositories { get; private set; } = Array.Empty<RepositoryInfo>();

    /// <summary>The names of the projects a policy can name.</summary>
    public IReadOnlyList<string> ProjectNames { get; private set; } = Array.Empty<string>();

    /// <summary>Loads <see cref="Repositories"/> and <see cref="ProjectNames"/>.</summary>
    protected async Task LoadChoicesAsync(CancellationToken cancellationToken)
    {
        Repositories = await PolicyScreenSupport.RetentionRepositoriesAsync(HttpContext.RequestServices.GetRequiredService<IRepositoryService>(), cancellationToken);
        ProjectNames = await PolicyScreenSupport.ProjectNamesAsync(HttpContext.RequestServices.GetRequiredService<IAccessAdministration>(), cancellationToken);
    }

    /// <summary>The rules of a policy in one line, for the lists.</summary>
    public static string Describe(RetentionPolicy p) => PolicyScreenSupport.DescribeRetention(p);
}
