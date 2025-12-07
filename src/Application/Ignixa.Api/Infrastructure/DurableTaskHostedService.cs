using Azure;
using DurableTask.Core;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Background service that manages the DurableTask worker lifecycle.
/// Starts the worker in the background without blocking application startup.
/// Stops it gracefully on shutdown.
/// Includes retry logic for storage permission propagation delays (RBAC can take up to 5 minutes).
/// </summary>
public class DurableTaskHostedService : BackgroundService
{
    private readonly TaskHubWorker _worker;
    private readonly IOrchestrationService _orchestrationService;
    private readonly ILogger<DurableTaskHostedService> _logger;

    private const int MaxRetries = 10;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(10);

    public DurableTaskHostedService(
        TaskHubWorker worker,
        IOrchestrationService orchestrationService,
        ILogger<DurableTaskHostedService> logger)
    {
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _orchestrationService = orchestrationService ?? throw new ArgumentNullException(nameof(orchestrationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting DurableTask worker...");

        // Retry loop for storage initialization (RBAC permissions can take up to 5 minutes to propagate)
        var initialized = await InitializeWithRetryAsync(stoppingToken);
        if (!initialized)
        {
            _logger.LogError("DurableTask worker failed to initialize after {MaxRetries} retries. Background jobs will not be available.", MaxRetries);
            return; // Don't crash the host - just disable DurableTask functionality
        }

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
            // Don't throw - allow other services to continue running
        }
    }

    private async Task<bool> InitializeWithRetryAsync(CancellationToken stoppingToken)
    {
        if (_orchestrationService is not DurableTask.AzureStorage.AzureStorageOrchestrationService azureService)
        {
            return true; // Non-Azure backends don't need retry logic
        }

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("Initializing Azure Storage orchestration service (attempt {Attempt}/{MaxRetries})...", attempt, MaxRetries);
                await azureService.CreateIfNotExistsAsync();
                _logger.LogInformation("Azure Storage orchestration service initialized successfully");
                return true;
            }
            catch (Exception ex) when (IsAuthorizationError(ex))
            {
                if (attempt == MaxRetries)
                {
                    _logger.LogError(ex, "Storage authorization failed after {MaxRetries} attempts. RBAC permissions may not be assigned correctly.", MaxRetries);
                    return false;
                }

                var delay = TimeSpan.FromSeconds(InitialRetryDelay.TotalSeconds * attempt); // 10s, 20s, 30s...
                _logger.LogWarning(
                    "Storage authorization failed (403). RBAC permissions may still be propagating. Retrying in {Delay}... (attempt {Attempt}/{MaxRetries})",
                    delay, attempt, MaxRetries);

                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("DurableTask initialization cancelled");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error initializing Azure Storage orchestration service");
                return false;
            }
        }

        return false;
    }

    private static bool IsAuthorizationError(Exception ex)
    {
        // Check for 403 Forbidden in the exception chain
        return ex.InnerException is RequestFailedException { Status: 403 }
            || ex.Message.Contains("AuthorizationFailure", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("not authorized", StringComparison.OrdinalIgnoreCase);
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
