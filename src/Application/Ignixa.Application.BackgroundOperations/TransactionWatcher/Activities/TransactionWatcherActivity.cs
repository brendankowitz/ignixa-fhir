// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.TransactionWatcher.Models;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.TransactionWatcher.Activities;

/// <summary>
/// DurableTask activity that scans for stalled transactions and commits them.
/// Multi-tenant aware: Scans all active tenants and routes to correct storage implementation.
/// </summary>
public class TransactionWatcherActivity : AsyncTaskActivity<TransactionWatcherActivityInput, TransactionWatcherActivityOutput>
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ITenantConfigurationStore _tenantConfigStore;
    private readonly ILogger<TransactionWatcherActivity> _logger;

    public TransactionWatcherActivity(
        IFhirRepositoryFactory repositoryFactory,
        ITenantConfigurationStore tenantConfigStore,
        ILogger<TransactionWatcherActivity> logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _tenantConfigStore = tenantConfigStore ?? throw new ArgumentNullException(nameof(tenantConfigStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<TransactionWatcherActivityOutput> ExecuteAsync(
        TaskContext context,
        TransactionWatcherActivityInput input)
    {
        _logger.LogDebug("Starting transaction watcher scan cycle");

        var scanStartTime = DateTimeOffset.UtcNow;
        int tenantsScanned = 0;
        int totalStalled = 0;
        int totalCommitted = 0;
        int totalFailed = 0;

        try
        {
            // Get all active tenants
            var tenants = await _tenantConfigStore.GetAllTenantsAsync(CancellationToken.None);

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
                tenantsScanned++;

                try
                {
                    _logger.LogDebug(
                        "Scanning tenant {TenantId} ({TenantName}) for stalled transactions",
                        tenant.TenantId,
                        tenant.DisplayName);

                    // Get repository for this tenant
                    var repository = await _repositoryFactory.GetRepositoryAsync(tenant.TenantId, CancellationToken.None);

                    // Query for stalled transactions
                    var stalledTransactions = await repository.GetStalledTransactionsAsync(
                        input.StallThreshold,
                        CancellationToken.None);

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
                            try
                            {
                                _logger.LogInformation(
                                    "Committing stalled transaction {TransactionId} for tenant {TenantId}",
                                    transactionId,
                                    tenant.TenantId);

                                await repository.CommitTransactionAsync(transactionId, CancellationToken.None);

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

            return new TransactionWatcherActivityOutput(
                TenantsScanned: tenantsScanned,
                StalledTransactionsFound: totalStalled,
                TransactionsCommitted: totalCommitted,
                TransactionsFailed: totalFailed,
                ScanDurationMs: scanDuration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during transaction watcher scan");

            var scanDuration = DateTimeOffset.UtcNow - scanStartTime;

            return new TransactionWatcherActivityOutput(
                TenantsScanned: tenantsScanned,
                StalledTransactionsFound: totalStalled,
                TransactionsCommitted: totalCommitted,
                TransactionsFailed: totalFailed,
                ScanDurationMs: scanDuration.TotalMilliseconds);
        }
    }
}
