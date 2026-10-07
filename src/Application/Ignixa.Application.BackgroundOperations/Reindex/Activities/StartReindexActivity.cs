using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class StartReindexActivity(
    ReindexLifecycleEventWriter lifecycle,
    ReindexJobUpdater jobs)
    : AsyncTaskActivity<StartReindexInput, StartReindexOutput>
{
    protected override async Task<StartReindexOutput> ExecuteAsync(
        TaskContext context,
        StartReindexInput input)
    {
        var ignored = await lifecycle.StartAsync(
            input.JobId,
            input.Targets,
            CancellationToken.None);
        await jobs.UpdateAsync(
            input.JobId,
            job =>
            {
                job.Status = "Running";
                job.StartDate ??= DateTimeOffset.UtcNow;
                job.Progress = new JsonObject
                {
                    ["phase"] = "BarrierDelay",
                    ["ignoredLifecycleEvents"] = new JsonArray(
                        ignored.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray())
                };
            },
            CancellationToken.None);
        return new StartReindexOutput(ignored);
    }
}
