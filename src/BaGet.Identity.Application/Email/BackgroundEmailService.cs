using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BaGet.Identity.Application.Email;

internal sealed class BackgroundEmailService : BackgroundService
{
    private readonly BackgroundEmailQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BackgroundEmailService> _logger;

    public BackgroundEmailService(BackgroundEmailQueue queue, IServiceScopeFactory scopes, ILogger<BackgroundEmailService> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var work in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    await work(scope.ServiceProvider);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Could not send a queued email.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
