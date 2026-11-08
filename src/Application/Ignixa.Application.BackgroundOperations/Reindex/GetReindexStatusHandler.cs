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

        var result = GetReindexJobsHandler.ToResult(job, options.Value, timeProvider);
        if (result.IsStale)
        {
            logger.LogError(
                "Reindex: job {JobId} is stale; last heartbeat {HeartbeatDate}",
                job.JobId,
                job.HeartbeatDate);
        }

        return result;
    }
}
