using System.Diagnostics;

namespace BaGet.Service.Service;

public sealed class BaGetService : BackgroundService
{
    private readonly ILogger<BaGetService> _logger;

    public BaGetService(ILogger<BaGetService> logger) =>
        _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await BaGet.Program.Main(Program.Arguments);
            }
            Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Message}", ex.Message);

            // Terminates this process and returns an exit code to the operating system.
            // This is required to avoid the 'BackgroundServiceExceptionBehavior', which
            // performs one of two scenarios:
            // 1. When set to "Ignore": will do nothing at all, errors cause zombie services.
            // 2. When set to "StopHost": will cleanly stop the host, and log errors.
            //
            // In order for the Windows Service Management system to leverage configured
            // recovery options, we need to terminate the process with a non-zero exit code.
            Dispose();
            Environment.Exit(1);
        }
    }

}
