using Ignixa.Application.Features.Conformance;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class ReindexStartupReconciler(
    IMediator mediator,
    IOptions<ReindexOptions> options,
    ILogger<ReindexStartupReconciler> logger)
{
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.AutoStart)
        {
            logger.LogInformation(
                "Reindex: startup reconciliation skipped because AutoStart is false");
            return;
        }

        CreateReindexJobResult result;
        try
        {
            result = await mediator.SendAsync(
                new CreateReindexJobCommand { Trigger = "Reconciliation" },
                cancellationToken);
        }
        catch (Exception exception) when (ReindexTriggerUnavailableException.IsOperational(exception))
        {
            throw new ReindexTriggerUnavailableException(
                "Reindex reconciliation is temporarily unavailable.",
                exception);
        }
        switch (result)
        {
            case ReindexJobCreatedResult created:
                ReindexMetrics.TriggerStarted("Reconciliation");
                logger.LogInformation(
                    "Reindex: startup reconciliation started job {JobId}",
                    created.JobId);
                break;
            case ActiveReindexJobResult:
            case NoReindexWorkResult:
            case ReindexDisabledResult:
            case ReindexProviderUnavailableResult:
                break;
            default:
                throw new InvalidOperationException(
                    $"Reindex startup reconciliation returned unexpected result {result.GetType().Name}.");
        }
    }
}
