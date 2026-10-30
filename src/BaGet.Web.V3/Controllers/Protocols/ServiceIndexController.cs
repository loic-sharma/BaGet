using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Protocol.Models;
using Microsoft.AspNetCore.Mvc;

namespace BaGet.Web.V3.Controllers.Protocols;

/// <summary>
/// The NuGet Service Index. This aids NuGet client to discover this server's services.
/// </summary>
public class ServiceIndexController : ControllerBase
{
    private readonly IServiceIndexService _serviceIndex;

    public ServiceIndexController(IServiceIndexService serviceIndex)
    {
        _serviceIndex = serviceIndex ?? throw new ArgumentNullException(nameof(serviceIndex));
    }

    // GET v3/index
    [HttpGet]
    public async Task<ServiceIndexResponse> GetAsync(CancellationToken cancellationToken)
    {
        return await _serviceIndex.GetAsync(cancellationToken);
    }
}
