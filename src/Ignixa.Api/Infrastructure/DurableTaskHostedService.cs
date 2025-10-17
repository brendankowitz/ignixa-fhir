using DurableTask.Core;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Hosted service that manages the DurableTask worker lifecycle.
/// Starts the worker when the application starts and stops it gracefully on shutdown.
/// </summary>
public class DurableTaskHostedService : IHostedService
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting DurableTask worker...");
        await _worker.StartAsync();
        _logger.LogInformation("DurableTask worker started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping DurableTask worker...");
        await _worker.StopAsync(isForced: false);
        _logger.LogInformation("DurableTask worker stopped successfully");
    }
}
