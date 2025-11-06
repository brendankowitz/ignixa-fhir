using DurableTask.Core;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Background service that manages the DurableTask worker lifecycle.
/// Starts the worker in the background without blocking application startup.
/// Stops it gracefully on shutdown.
/// </summary>
public class DurableTaskHostedService : BackgroundService
{
    private readonly TaskHubWorker _worker;
    private readonly ILogger<DurableTaskHostedService> _logger;

    public DurableTaskHostedService(
        TaskHubWorker worker,
        ILogger<DurableTaskHostedService> logger)
    {
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting DurableTask worker...");
        try
        {
            await _worker.StartAsync();
            _logger.LogInformation("DurableTask worker started successfully");

            // Keep the service running until cancellation is requested
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("DurableTask worker execution cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while running DurableTask worker");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping DurableTask worker...");
        try
        {
            await _worker.StopAsync(isForced: false);
            _logger.LogInformation("DurableTask worker stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while stopping DurableTask worker");
        }
        finally
        {
            await base.StopAsync(cancellationToken);
        }
    }
}
