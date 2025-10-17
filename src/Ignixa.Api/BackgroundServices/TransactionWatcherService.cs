// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Options;
using Ignixa.Api.Configuration;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;

namespace Ignixa.Api.BackgroundServices;

/// <summary>
/// Background service that monitors for stalled transactions and automatically commits them.
/// Runs periodically based on configured ScanInterval.
/// Multi-tenant aware: Scans all active tenants and routes to correct storage implementation (FileSystem or SQL).
/// </summary>
public sealed class TransactionWatcherService : IHostedService, IDisposable
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ITenantConfigurationStore _tenantConfigStore;
    private readonly TransactionWatcherOptions _options;
    private readonly ILogger<TransactionWatcherService> _logger;
    private Timer? _timer;
    private int _isExecuting;

    public TransactionWatcherService(
        IFhirRepositoryFactory repositoryFactory,
        ITenantConfigurationStore tenantConfigStore,
        IOptions<TransactionWatcherOptions> options,
        ILogger<TransactionWatcherService> logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _tenantConfigStore = tenantConfigStore ?? throw new ArgumentNullException(nameof(tenantConfigStore));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Transaction watcher is disabled (TransactionWatcher:Enabled = false)");
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "Transaction watcher starting (ScanInterval: {ScanInterval}, StallThreshold: {StallThreshold})",
            _options.ScanInterval,
            _options.StallThreshold);

        // Start timer with configured scan interval
        _timer = new Timer(
            ExecuteScanAsync,
            null,
            TimeSpan.Zero,  // Start immediately
            _options.ScanInterval);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Transaction watcher stopping");

        _timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        GC.SuppressFinalize(this);
    }

    private async void ExecuteScanAsync(object? state)
    {
        // Prevent overlapping executions
        if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
        {
            _logger.LogDebug("Skipping scan - previous scan still running");
            return;
        }

        try
        {
            await ScanAndCommitStalledTransactionsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during transaction watcher scan");
        }
        finally
        {
            Interlocked.Exchange(ref _isExecuting, 0);
        }
    }

    private async Task ScanAndCommitStalledTransactionsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting transaction watcher scan cycle");

        var scanStartTime = DateTimeOffset.UtcNow;
        int totalStalled = 0;
        int totalCommitted = 0;
        int totalFailed = 0;

        try
        {
            // Get all active tenants
            var tenants = await _tenantConfigStore.GetAllTenantsAsync(cancellationToken);

            // Filter out system partition (Partition 0)
            var activeTenants = tenants
                .Where(t => !t.IsSystemPartition && t.IsActive)
                .ToList();

            _logger.LogDebug(
                "Scanning {Count} active tenants for stalled transactions",
                activeTenants.Count);

            // Scan each tenant's repository
            foreach (var tenant in activeTenants)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Transaction watcher scan cancelled");
                    break;
                }

                try
                {
                    _logger.LogDebug(
                        "Scanning tenant {TenantId} ({TenantName}) for stalled transactions",
                        tenant.TenantId,
                        tenant.DisplayName);

                    // Get repository for this tenant
                    var repository = await _repositoryFactory.GetRepositoryAsync(tenant.TenantId, cancellationToken);

                    // Query for stalled transactions
                    var stalledTransactions = await repository.GetStalledTransactionsAsync(
                        _options.StallThreshold,
                        cancellationToken);

                    if (stalledTransactions.Count > 0)
                    {
                        _logger.LogWarning(
                            "Found {Count} stalled transactions for tenant {TenantId} ({TenantName})",
                            stalledTransactions.Count,
                            tenant.TenantId,
                            tenant.DisplayName);

                        totalStalled += stalledTransactions.Count;

                        // Commit each stalled transaction
                        foreach (var transactionId in stalledTransactions)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                break;
                            }

                            try
                            {
                                _logger.LogInformation(
                                    "Committing stalled transaction {TransactionId} for tenant {TenantId}",
                                    transactionId,
                                    tenant.TenantId);

                                await repository.CommitTransactionAsync(transactionId, cancellationToken);

                                totalCommitted++;

                                _logger.LogInformation(
                                    "Successfully committed stalled transaction {TransactionId} for tenant {TenantId}",
                                    transactionId,
                                    tenant.TenantId);
                            }
                            catch (Exception ex)
                            {
                                totalFailed++;

                                _logger.LogError(
                                    ex,
                                    "Failed to commit stalled transaction {TransactionId} for tenant {TenantId} - will retry on next scan",
                                    transactionId,
                                    tenant.TenantId);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogDebug(
                            "No stalled transactions found for tenant {TenantId}",
                            tenant.TenantId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error scanning tenant {TenantId} ({TenantName}) for stalled transactions",
                        tenant.TenantId,
                        tenant.DisplayName);
                }
            }

            var scanDuration = DateTimeOffset.UtcNow - scanStartTime;

            if (totalStalled > 0)
            {
                _logger.LogInformation(
                    "Transaction watcher scan complete: {Duration}ms, {TotalStalled} stalled, {TotalCommitted} committed, {TotalFailed} failed",
                    scanDuration.TotalMilliseconds,
                    totalStalled,
                    totalCommitted,
                    totalFailed);
            }
            else
            {
                _logger.LogDebug(
                    "Transaction watcher scan complete: {Duration}ms, no stalled transactions found",
                    scanDuration.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during transaction watcher scan");
        }
    }
}
