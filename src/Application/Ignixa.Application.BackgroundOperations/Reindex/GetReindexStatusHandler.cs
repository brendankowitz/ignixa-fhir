using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class GetReindexStatusHandler(
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    IOptions<ReindexOptions> options,
    TimeProvider timeProvider,
    ILogger<GetReindexStatusHandler> logger)
    : IRequestHandler<GetReindexStatusQuery, ReindexStatusResult?>
{
    public async Task<ReindexStatusResult?> HandleAsync(
        GetReindexStatusQuery request,
        CancellationToken cancellationToken)
    {
        // Job ids are shared by every job type; a job of another type is reported as absent.
        var job = await repository.GetAsync(
            request.JobId,
            SystemConstants.GlobalTenantId,
            (int)BackgroundJobType.Reindex,
            cancellationToken);
        if (job is null)
        {
            return null;
        }

        var isStale = (job.Status.Equals("Queued", StringComparison.OrdinalIgnoreCase) ||
                job.Status.Equals("Running", StringComparison.OrdinalIgnoreCase)) &&
            timeProvider.GetUtcNow() - job.HeartbeatDate > options.Value.StaleJobTimeout;
        if (isStale)
        {
            logger.LogError(
                "Reindex: job {JobId} is stale; last heartbeat {HeartbeatDate}",
                job.JobId,
                job.HeartbeatDate);
        }

        return new ReindexStatusResult(
            job.JobId,
            job.Status,
            job.CreateDate,
            job.StartDate,
            job.EndDate,
            job.HeartbeatDate,
            isStale,
            job.ErrorMessage,
            job.Progress,
            job.Result,
            job.Definition);
    }
}
