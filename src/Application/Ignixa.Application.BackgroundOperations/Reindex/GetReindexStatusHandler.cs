using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class GetReindexStatusHandler(
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    IOptions<ReindexOptions> options,
    TimeProvider timeProvider)
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
        return job is null ? null : GetReindexJobsHandler.ToResult(job, options.Value, timeProvider);
    }
}
