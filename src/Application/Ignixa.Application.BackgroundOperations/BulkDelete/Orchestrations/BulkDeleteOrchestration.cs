// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkDelete.Activities;
using Ignixa.Application.BackgroundOperations.BulkDelete.Models;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Orchestrations;

/// <summary>
/// Durable Task orchestration for <c>$bulk-delete</c>. Processes the snapshotted resource types in order,
/// one <see cref="BulkDeleteBatchActivity"/> page at a time, then records the outcome with
/// <see cref="CompleteBulkDeleteJobActivity"/>.
/// </summary>
/// <remarks>
/// <para>
/// Batches are sequential so a cancel (orchestration termination) stops new work after at most one
/// in-flight page, and so restart-mode traversal can detect a page that made no progress.
/// </para>
/// <para>
/// Every <see cref="BatchesPerExecution"/> batches the orchestration continues as new, carrying its
/// position (type index, purge cursor, previous first match) and cumulative counts in the input. History,
/// and so replay cost, stays bounded however many resources a job deletes. The instance ID is unchanged,
/// so status and cancel address every execution alike.
/// </para>
/// <para>
/// A batch that reports <see cref="BulkDeleteBatchOutput.Superseded"/> means the job is already terminal
/// (cancelled); the orchestration stops without finalizing. Any other failure is persisted as a Failed
/// job; if even that cannot be persisted the orchestration itself fails, and the status handler
/// reconciles the job from the orchestration state.
/// </para>
/// </remarks>
public class BulkDeleteOrchestration : TaskOrchestration<BulkDeleteOrchestrationOutput, BulkDeleteOrchestrationInput>
{
    /// <summary>
    /// Batches run by one execution before it continues as new. Each batch adds a few history events.
    /// </summary>
    public const int BatchesPerExecution = 100;

    public override async Task<BulkDeleteOrchestrationOutput> RunTask(
        OrchestrationContext context,
        BulkDeleteOrchestrationInput input)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        var state = new TraversalState(input);
        try
        {
            var outcome = await TraverseAsync(context, input, state);
            switch (outcome)
            {
                case TraversalOutcome.Superseded:
                    return new BulkDeleteOrchestrationOutput(Success: false, Superseded: true, state.Totals, ErrorMessage: null);
                case TraversalOutcome.ContinuedAsNew:
                    context.ContinueAsNew(input with
                    {
                        TypeIndex = state.TypeIndex,
                        ContinuationToken = state.ContinuationToken,
                        PreviousFirstMatchKey = state.PreviousFirstMatchKey,
                        CarriedCounts = new Dictionary<string, long>(state.Totals, StringComparer.Ordinal),
                    });

                    // DurableTask discards this execution's output; the next execution produces the real one.
                    return new BulkDeleteOrchestrationOutput(Success: false, Superseded: false, state.Totals, ErrorMessage: null);
            }
        }
        catch (Exception ex)
        {
            var message = state.TypeIndex < input.ResourceTypes.Count
                ? $"Bulk delete failed while processing '{input.ResourceTypes[state.TypeIndex]}': {ex.Message}"
                : $"Bulk delete failed: {ex.Message}";
            var recorded = await CompleteAsync(context, input, success: false, state.Totals, message);
            return new BulkDeleteOrchestrationOutput(Success: false, Superseded: !recorded, state.Totals, message);
        }

        var completed = await CompleteAsync(context, input, success: true, state.Totals, errorMessage: null);
        return new BulkDeleteOrchestrationOutput(Success: completed, Superseded: !completed, state.Totals, ErrorMessage: null);
    }

    private static async Task<TraversalOutcome> TraverseAsync(
        OrchestrationContext context,
        BulkDeleteOrchestrationInput input,
        TraversalState state)
    {
        // Soft and hard delete remove each page's matches, so the next batch re-reads the first page.
        // Purge keeps current versions, so it pages forward with the provider's cursor instead.
        var restartMode = input.Mode != BulkDeleteMode.PurgeHistory;
        var batches = 0;
        for (; state.TypeIndex < input.ResourceTypes.Count; state.StartNextType())
        {
            BulkDeleteBatchOutput output;
            do
            {
                // Checked before scheduling, so an execution only continues as new when work remains.
                if (batches == BatchesPerExecution)
                {
                    return TraversalOutcome.ContinuedAsNew;
                }

                var batch = new BulkDeleteBatchInput(
                    input.JobId,
                    input.TenantId,
                    input.ResourceTypes[state.TypeIndex],
                    input.SearchQuery,
                    input.Mode,
                    input.ExcludedResourceTypes,
                    input.RemoveReferences,
                    input.BatchSize,
                    state.ContinuationToken,
                    new Dictionary<string, long>(state.Totals, StringComparer.Ordinal));
                output = await context.ScheduleWithRetry<BulkDeleteBatchOutput>(
                    typeof(BulkDeleteBatchActivity), CreateRetryOptions(), batch);
                batches++;
                if (output.Superseded)
                {
                    return TraversalOutcome.Superseded;
                }

                foreach (var (type, count) in output.DeletedCounts)
                {
                    state.Totals[type] = state.Totals.GetValueOrDefault(type) + count;
                }

                if (restartMode && output.HasMore && output.FirstMatchKey == state.PreviousFirstMatchKey)
                {
                    throw new InvalidOperationException(
                        $"A batch made no progress: '{output.FirstMatchKey}' still leads the matches after it was processed.");
                }

                state.PreviousFirstMatchKey = output.FirstMatchKey;
                state.ContinuationToken = output.NextContinuationToken;
            }
            while (output.HasMore);
        }

        return TraversalOutcome.Completed;
    }

    private static Task<bool> CompleteAsync(
        OrchestrationContext context,
        BulkDeleteOrchestrationInput input,
        bool success,
        Dictionary<string, long> totals,
        string? errorMessage) =>
        context.ScheduleWithRetry<bool>(
            typeof(CompleteBulkDeleteJobActivity),
            CreateRetryOptions(),
            new CompleteBulkDeleteJobInput(input.JobId, input.TenantId, success,
                new Dictionary<string, long>(totals, StringComparer.Ordinal), errorMessage));

    private static RetryOptions CreateRetryOptions() => new(TimeSpan.FromSeconds(5), maxNumberOfAttempts: 3)
    {
        BackoffCoefficient = 2,
    };

    private enum TraversalOutcome
    {
        Completed,
        Superseded,
        ContinuedAsNew,
    }

    /// <summary>
    /// The traversal position and totals, seeded from the input a previous execution carried over.
    /// </summary>
    private sealed class TraversalState(BulkDeleteOrchestrationInput input)
    {
        public int TypeIndex { get; set; } = input.TypeIndex;

        public string? ContinuationToken { get; set; } = input.ContinuationToken;

        public string? PreviousFirstMatchKey { get; set; } = input.PreviousFirstMatchKey;

        public Dictionary<string, long> Totals { get; } =
            new(input.CarriedCounts ?? new Dictionary<string, long>(), StringComparer.Ordinal);

        public void StartNextType()
        {
            TypeIndex++;
            ContinuationToken = null;
            PreviousFirstMatchKey = null;
        }
    }
}
