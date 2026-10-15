using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class GetReindexJobsHandler(
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    IOptions<ReindexOptions> options,
    TimeProvider timeProvider)
    : IRequestHandler<GetReindexJobsQuery, IReadOnlyList<ReindexStatusResult>>
{
    public async Task<IReadOnlyList<ReindexStatusResult>> HandleAsync(
        GetReindexJobsQuery request,
        CancellationToken cancellationToken)
    {
        var jobs = await repository.ListAsync((int)BackgroundJobType.Reindex, cancellationToken);
        var active = jobs
            .Where(IsActive)
            .OrderByDescending(job => job.CreateDate);
        var terminal = jobs
            .Where(job => !IsActive(job))
            .OrderByDescending(job => job.EndDate ?? job.CreateDate)
            .Take(request.TerminalJobLimit);

        return active
            .Concat(terminal)
            .Select(job => ToResult(job, options.Value, timeProvider))
            .ToArray();
    }

    internal static ReindexStatusResult ToResult(
        BackgroundJob<ReindexJobDefinition> job,
        ReindexOptions options,
        TimeProvider timeProvider)
    {
        var progress = ReindexProgress.FromJson(job.Progress);
        // The barrier delay is one durable wait with no activity to heartbeat, so it does not count as silence.
        var allowedSilence = options.StaleJobTimeout +
            (progress?.Phase == ReindexPhase.BarrierDelay ? options.BarrierDelay : TimeSpan.Zero);
        return new(
            job.JobId,
            job.GetStatus(),
            job.CreateDate,
            job.StartDate,
            job.EndDate,
            job.HeartbeatDate,
            IsActive(job) && timeProvider.GetUtcNow() - job.HeartbeatDate > allowedSilence,
            job.ErrorMessage,
            progress,
            job.Definition);
    }

    private static bool IsActive(BackgroundJob<ReindexJobDefinition> job) => !job.IsTerminal();
}
