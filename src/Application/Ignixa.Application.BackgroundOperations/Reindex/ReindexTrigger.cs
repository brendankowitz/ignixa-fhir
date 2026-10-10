using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Infrastructure;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// Starts reindex jobs automatically: once after an activation, and on every conformance sync tick, which
/// also recovers a job whose orchestration died. The tick reads the job table twice and the projection once,
/// under the activation lock, before it takes the singleton job lock, so an idle server and the steady state
/// after a Failed or Cancelled job never lock.
/// </summary>
/// <remarks>
/// Operational failures (storage, the singleton lock, the orchestration runtime) are logged and metered once
/// and retried by the next tick; programmer errors and the caller's own cancellation propagate.
/// </remarks>
public sealed class ReindexTrigger(
    IMediator mediator,
    IBackgroundJobRepository<ReindexJobDefinition> jobRepository,
    ConformanceState conformanceState,
    ReindexJobReconciler reconciler,
    CompositeRepositoryFactory repositoryFactory,
    IOptions<ReindexOptions> options,
    ILogger<ReindexTrigger> logger) : IReindexTrigger
{
    private static readonly EventId ActivationTriggerDeferred = new(4201, "ReindexActivationTriggerDeferred");
    private static readonly EventId ReconciliationDeferred = new(4202, "ReindexReconciliationDeferred");
    private static readonly List<string> FinishedStatuses = ["Completed", "Failed", "Cancelled"];

    public async Task<ReindexTriggerResult> RequestReindexAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        if (!options.Value.AutoStart)
        {
            logger.LogInformation(
                "Reindex: automatic trigger ignored because AutoStart is false; reason {Reason}",
                reason);
            return new ReindexTriggerResult(
                null,
                false,
                "Automatic reindex is disabled; parameters remain Pending.");
        }

        try
        {
            if (await DescribeUnavailabilityAsync(cancellationToken) is { } unavailable)
            {
                return new ReindexTriggerResult(null, false, unavailable);
            }

            var result = await mediator.SendAsync(
                new CreateReindexJobCommand { Trigger = "Activation" },
                cancellationToken);
            return result switch
            {
                ReindexJobCreatedResult created => RecordStarted("Activation", created.JobId, reason),
                ActiveReindexJobResult active => new ReindexTriggerResult(active.ActiveJobId, true, null),
                NoReindexWorkResult noWork => new ReindexTriggerResult(null, false, noWork.ErrorMessage),
                InvalidReindexRequestResult invalid => throw new InvalidOperationException(invalid.ErrorMessage),
                _ => throw new InvalidOperationException(
                    $"Unsupported automatic reindex result {result.GetType().Name}.")
            };
        }
        catch (Exception exception) when (IsOperational(exception, cancellationToken))
        {
            ReindexMetrics.RecordTriggerFailure("Activation");
            logger.LogError(
                ActivationTriggerDeferred,
                exception,
                "Reindex: activation trigger failed operationally; periodic reconciliation will retry; reason {Reason}",
                reason);
            return new ReindexTriggerResult(
                null,
                false,
                "The automatic reindex trigger failed; periodic reconciliation will retry.",
                Deferred: true);
        }
    }

    /// <summary>
    /// One tick: recover the active job if its orchestration is gone, otherwise start a job when
    /// <see cref="ReindexStartRule"/> says one is due. Runs at startup and on every conformance sync.
    /// </summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            var active = await jobRepository.GetActiveAsync(
                (int)BackgroundJobType.Reindex,
                cancellationToken);
            if (active is not null)
            {
                await reconciler.ReconcileAsync(active, cancellationToken);
                return;
            }

            if (!options.Value.AutoStart)
            {
                logger.LogDebug("Reindex: periodic reconciliation starts no job because AutoStart is false");
                return;
            }

            var latestFinished = await jobRepository.GetLatestAsync(
                (int)BackgroundJobType.Reindex,
                FinishedStatuses,
                cancellationToken);
            if (!await HasPendingActivationAfterAsync(latestFinished?.Definition.TargetEventId, cancellationToken))
            {
                return;
            }

            if (await DescribeUnavailabilityAsync(cancellationToken) is { } unavailable)
            {
                logger.LogDebug("Reindex: periodic reconciliation starts no job; {Reason}", unavailable);
                return;
            }

            var result = await mediator.SendAsync(
                new CreateReindexJobCommand { Trigger = "Reconciliation" },
                cancellationToken);
            switch (result)
            {
                case ReindexJobCreatedResult created:
                    RecordStarted("Reconciliation", created.JobId, "periodic reconciliation");
                    break;
                case ActiveReindexJobResult:
                case NoReindexWorkResult:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Reindex periodic reconciliation returned unexpected result {result.GetType().Name}.");
            }
        }
        catch (Exception exception) when (IsOperational(exception, cancellationToken))
        {
            ReindexMetrics.RecordReconciliationFailure();
            logger.LogError(
                ReconciliationDeferred,
                exception,
                "Reindex: periodic reconciliation failed operationally; the next conformance sync will retry");
        }
    }

    private async Task<bool> HasPendingActivationAfterAsync(
        long? latestFinishedTargetEventId,
        CancellationToken cancellationToken)
    {
        // The live projection is only ever enumerated under the activation lock; a concurrent Apply would
        // otherwise invalidate the enumeration.
        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            var pendingActivations = conformanceState.AllSearchParameters.Values
                .Where(parameter => parameter.Status == SearchParameterStatus.Pending)
                .Select(parameter => parameter.ActivationEventId)
                .ToArray();
            return ReindexStartRule.RequiresJob(pendingActivations, latestFinishedTargetEventId);
        }
    }

    private async Task<string?> DescribeUnavailabilityAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return "Reindex is unavailable because the feature is disabled; parameters remain Pending.";
        }

        return await repositoryFactory.FindTenantWithoutReindexSupportAsync(cancellationToken) is int tenantId
            ? $"Reindex is unavailable for tenant {tenantId}; parameters remain Pending."
            : null;
    }

    private ReindexTriggerResult RecordStarted(string trigger, string jobId, string reason)
    {
        ReindexMetrics.TriggerStarted(trigger);
        logger.LogInformation(
            "Reindex: {Trigger} trigger started job {JobId}; reason {Reason}",
            trigger,
            jobId,
            reason);
        return new ReindexTriggerResult(jobId, false, null);
    }

    // Only operational failures defer: they recover on a later tick. The listed types are our own
    // invariants and argument contracts, which no retry fixes; the caller's cancellation is its own.
    private static bool IsOperational(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            ArgumentException
                or InvalidOperationException
                or NullReferenceException
                or InvalidCastException
                or NotSupportedException
                or NotImplementedException
                or IndexOutOfRangeException
                or KeyNotFoundException => false,
            _ => true
        };
}
