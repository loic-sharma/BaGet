using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace BaGet.Core;

/// <summary>
/// Runs the database copy accepted by <see cref="DatabaseCopyService.TryQueue"/>; one at a time,
/// because the service accepts one request at a time.
/// </summary>
public class DatabaseCopyWorker : BackgroundService
{
    private readonly DatabaseCopyService _service;

    public DatabaseCopyWorker(DatabaseCopyService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        try
        {
            await foreach (var request in _service.DequeueAllAsync(stoppingToken))
            {
                await _service.RunAsync(request, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
