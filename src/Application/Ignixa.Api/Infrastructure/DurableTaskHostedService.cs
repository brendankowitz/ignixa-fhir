using Azure;
using DurableTask.Core;
using DurableTask.SqlServer;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Background service that manages the DurableTask worker lifecycle.
/// Starts the worker in the background without blocking application startup.
/// Stops it gracefully on shutdown.
/// Includes retry logic for storage permission propagation delays (RBAC can take up to 5 minutes).
/// Automatically initializes database schema for SqlServer provider.
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
        // Handle SqlServer provider - initialize schema
        if (_orchestrationService is SqlOrchestrationService sqlService)
        {
            return await InitializeSqlServerAsync(sqlService, stoppingToken);
        }

        // Handle Azure Storage provider - may need retry for RBAC propagation
        if (_orchestrationService is DurableTask.AzureStorage.AzureStorageOrchestrationService azureService)
        {
            return await InitializeAzureStorageAsync(azureService, stoppingToken);
        }

        // FileSystem and InMemory providers don't need initialization
        return true;
    }

    private async Task<bool> InitializeSqlServerAsync(SqlOrchestrationService sqlService, CancellationToken stoppingToken)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("Initializing SQL Server orchestration service schema (attempt {Attempt}/{MaxRetries})...", attempt, MaxRetries);
                await sqlService.CreateIfNotExistsAsync();
                _logger.LogInformation("SQL Server orchestration service schema initialized successfully");
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("DurableTask SQL Server initialization cancelled");
                return false;
            }
            catch (Exception ex) when (IsSqlServerTransientError(ex))
            {
                if (attempt == MaxRetries)
                {
                    _logger.LogError(ex, "SQL Server orchestration service failed to initialize after {MaxRetries} attempts", MaxRetries);
                    return false;
                }

                var delay = TimeSpan.FromSeconds(InitialRetryDelay.TotalSeconds * attempt);
                _logger.LogWarning(
                    ex,
                    "SQL Server transient error during initialization. Retrying in {Delay}... (attempt {Attempt}/{MaxRetries})",
                    delay, attempt, MaxRetries);

                await Task.Delay(delay, stoppingToken);
            }
            catch (Exception ex)
            {
                // Non-transient errors (configuration, authentication, permissions) - fail fast
                _logger.LogError(ex, "SQL Server orchestration service initialization failed with non-transient error");
                return false;
            }
        }

        return false;
    }

    private async Task<bool> InitializeAzureStorageAsync(DurableTask.AzureStorage.AzureStorageOrchestrationService azureService, CancellationToken stoppingToken)
    {
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

                var delay = TimeSpan.FromSeconds(InitialRetryDelay.TotalSeconds * attempt);
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

    private static bool IsSqlServerTransientError(Exception ex)
    {
        // Check for SqlException with transient error numbers
        // See: https://learn.microsoft.com/en-us/azure/azure-sql/database/troubleshoot-common-errors-issues
        if (ex is Microsoft.Data.SqlClient.SqlException sqlEx)
        {
            // Transient SQL Server error numbers
            return sqlEx.Number switch
            {
                -2 => true,     // Timeout expired
                20 => true,     // Instance does not support encryption
                64 => true,     // Connection was successfully established but error occurred during login
                233 => true,    // Connection initialization error
                10053 => true,  // Connection was aborted
                10054 => true,  // Connection was forcibly closed
                10060 => true,  // Connection timeout
                40143 => true,  // Connection could not be initialized
                40197 => true,  // Service has encountered an error processing request
                40501 => true,  // Service is busy
                40613 => true,  // Database is not currently available
                49918 => true,  // Not enough resources to process request
                49919 => true,  // Cannot process create or update request
                49920 => true,  // Cannot process request due to too many operations
                _ => false
            };
        }

        // Check inner exception
        if (ex.InnerException is not null)
        {
            return IsSqlServerTransientError(ex.InnerException);
        }

        // Check for timeout patterns in message (fallback)
        return ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            && !ex.Message.Contains("login failed", StringComparison.OrdinalIgnoreCase);
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
