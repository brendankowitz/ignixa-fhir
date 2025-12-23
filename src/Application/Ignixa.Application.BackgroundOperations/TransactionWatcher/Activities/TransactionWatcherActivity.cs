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
/// DurableTask activity that scans a single tenant for stalled transactions and commits them.
/// A transaction is considered stalled if it hasn't been committed and exceeds the stall threshold
/// based on last heartbeat or file modification time.
/// </summary>
public class TransactionWatcherActivity(
    IFhirRepositoryFactory repositoryFactory,
    ILogger<TransactionWatcherActivity> logger)
    : AsyncTaskActivity<TransactionWatcherActivityInput, TransactionWatcherActivityOutput>
{
    private readonly IFhirRepositoryFactory _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
    private readonly ILogger<TransactionWatcherActivity> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    protected override async Task<TransactionWatcherActivityOutput> ExecuteAsync(
        TaskContext context,
        TransactionWatcherActivityInput input)
    {
        _logger.LogInformation(
            "Starting transaction watcher activity: TenantId={TenantId}, StallThreshold={StallThreshold}",
            input.TenantId,
            input.StallThreshold);

        int stalledCount = 0;
        int committedCount = 0;
        int failedCount = 0;

        try
        {
            var repository = await _repositoryFactory.GetRepositoryAsync(input.TenantId, CancellationToken.None);

            var stalledTransactions = await repository.GetStalledTransactionsAsync(
                input.StallThreshold,
                CancellationToken.None);

            stalledCount = stalledTransactions.Count;

            if (stalledCount == 0)
            {
                _logger.LogDebug("No stalled transactions found for tenant {TenantId}", input.TenantId);
                return new TransactionWatcherActivityOutput(
                    TenantId: input.TenantId,
                    StalledCount: 0,
                    CommittedCount: 0,
                    FailedCount: 0,
                    ErrorMessage: null);
            }

            _logger.LogWarning(
                "Found {Count} stalled transactions for tenant {TenantId}",
                stalledCount,
                input.TenantId);

            foreach (var transactionId in stalledTransactions)
            {
                try
                {
                    _logger.LogInformation(
                        "Committing stalled transaction {TransactionId} for tenant {TenantId}",
                        transactionId,
                        input.TenantId);

                    await repository.CommitTransactionAsync(transactionId, CancellationToken.None);

                    committedCount++;

                    _logger.LogInformation(
                        "Successfully committed stalled transaction {TransactionId} for tenant {TenantId}",
                        transactionId,
                        input.TenantId);
                }
                catch (Exception ex)
                {
                    failedCount++;

                    _logger.LogError(
                        ex,
                        "Failed to commit stalled transaction {TransactionId} for tenant {TenantId} - will retry on next scan",
                        transactionId,
                        input.TenantId);
                }
            }

            _logger.LogInformation(
                "Completed transaction watcher activity: TenantId={TenantId}, Stalled={Stalled}, Committed={Committed}, Failed={Failed}",
                input.TenantId,
                stalledCount,
                committedCount,
                failedCount);

            return new TransactionWatcherActivityOutput(
                TenantId: input.TenantId,
                StalledCount: stalledCount,
                CommittedCount: committedCount,
                FailedCount: failedCount,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Fatal error during transaction watcher activity for tenant {TenantId}",
                input.TenantId);

            return new TransactionWatcherActivityOutput(
                TenantId: input.TenantId,
                StalledCount: stalledCount,
                CommittedCount: committedCount,
                FailedCount: failedCount,
                ErrorMessage: ex.Message);
        }
    }
}
