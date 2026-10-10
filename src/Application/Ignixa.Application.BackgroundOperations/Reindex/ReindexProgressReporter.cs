using System.Text.Json.Nodes;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// Writes routine progress and heartbeats without the singleton job lock. A job that is already terminal,
/// or becomes terminal between the read and the write, is left untouched and reported as closed.
/// </summary>
public sealed class ReindexProgressReporter(
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    TimeProvider timeProvider)
{
    internal static void InitializeBarrierDelay(
        BackgroundJob<ReindexJobDefinition> job,
        IReadOnlyList<int> tenantIds,
        IReadOnlyList<string> ignoredLifecycleEvents,
        DateTimeOffset now)
    {
        job.Status = "Running";
        job.StartDate ??= now;
        var progress = new JsonObject
        {
            ["phase"] = "BarrierDelay",
            ["ignoredLifecycleEvents"] = new JsonArray(
                ignoredLifecycleEvents
                    .Select(value => (JsonNode?)JsonValue.Create(value))
                    .ToArray())
        };
        foreach (var tenantId in tenantIds)
        {
            _ = GetOrAddTenant(progress, tenantId);
        }

        Recalculate(progress, job.Status);
        job.Progress = progress;
    }

    public Task<bool> HeartbeatAsync(string jobId, CancellationToken cancellationToken) =>
        UpdateAsync(jobId, _ => { }, cancellationToken);

    public Task<bool> ReportAsync(
        PersistReindexProgressInput input,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            input.JobId,
            job =>
            {
                var progress = EnsureProgress(job.Progress);
                // DurableTask may redeliver an activity whose write committed before its response was lost.
                if (input.Sequence <= GetInt64(progress, "sequence"))
                {
                    return;
                }

                progress["sequence"] = input.Sequence;
                SetPhase(progress, input.Phase);
                foreach (var state in input.Tenants)
                {
                    var tenant = GetOrAddTenant(progress, state.TenantId);
                    tenant["status"] = state.Phase;
                    tenant["cutoffTransactionId"] = state.CutoffTransactionId;
                    tenant["cutoffSurrogateId"] = state.CutoffSurrogateId;
                    tenant["visibleWatermark"] = state.VisibleWatermark;
                    tenant["plannerCursor"] = state.PlannerCursor;
                    tenant["resourcesToReindex"] = state.ResourcesToReindex;
                    tenant["resourcesRead"] = state.ResourcesRead;
                    tenant["resourcesReindexed"] = state.ResourcesReindexed;
                    tenant["conflicts"] = state.Conflicts;
                    tenant["failedResources"] = state.FailedResourceCount;
                    tenant["errorMessage"] = state.ErrorMessage;
                }

                Recalculate(progress, job.Status);
                job.Progress = progress;
            },
            cancellationToken);

    private async Task<bool> UpdateAsync(
        string jobId,
        Action<BackgroundJob<ReindexJobDefinition>> update,
        CancellationToken cancellationToken)
    {
        var job = await repository.GetAsync(jobId, ReindexJobs.GlobalTenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Reindex job {jobId} does not exist.");
        if (ReindexJobs.IsTerminal(job.Status))
        {
            return false;
        }

        update(job);
        job.HeartbeatDate = timeProvider.GetUtcNow();
        try
        {
            await repository.UpdateAsync(job, ReindexJobs.GlobalTenantId, cancellationToken);
            return true;
        }
        catch (BackgroundJobUpdateConflictException)
        {
            // The job closed between the read and the write; the terminal state is authoritative.
            return false;
        }
    }

    private static JsonObject EnsureProgress(JsonNode? progress) =>
        progress as JsonObject ?? new JsonObject();

    private static JsonObject GetOrAddTenant(JsonObject progress, int tenantId)
    {
        var tenants = progress["tenants"] as JsonArray;
        if (tenants is null)
        {
            tenants = [];
            progress["tenants"] = tenants;
        }

        var tenant = tenants
            .OfType<JsonObject>()
            .SingleOrDefault(item => item["tenantId"]?.GetValue<int>() == tenantId);
        if (tenant is not null)
        {
            return tenant;
        }

        tenant = new JsonObject
        {
            ["tenantId"] = tenantId,
            ["status"] = "BarrierDelay",
            ["resourcesToReindex"] = 0,
            ["resourcesRead"] = 0,
            ["resourcesReindexed"] = 0,
            ["conflicts"] = 0,
            ["failedResources"] = 0
        };
        tenants.Add(tenant);
        return tenant;
    }

    private static void Recalculate(JsonObject progress, string status)
    {
        var tenants = progress["tenants"] as JsonArray;
        var tenantObjects = tenants?.OfType<JsonObject>().ToArray() ?? [];
        var total = tenantObjects.Sum(tenant => GetInt64(tenant, "resourcesToReindex"));
        var reindexed = tenantObjects.Sum(tenant => GetInt64(tenant, "resourcesReindexed"));
        progress["totalResourcesToReindex"] = total;
        progress["resourcesSuccessfullyReindexed"] = reindexed;
        progress["conflicts"] = tenantObjects.Sum(tenant => GetInt64(tenant, "conflicts"));
        progress["progress"] = status == "Completed"
            ? 100
            : total == 0
                ? 0
                : Math.Min(99.9, reindexed * 100.0 / total);
    }

    private static long GetInt64(JsonObject value, string propertyName)
    {
        if (value[propertyName] is not JsonValue number)
        {
            return 0;
        }

        if (number.TryGetValue<long>(out var int64))
        {
            return int64;
        }

        return number.TryGetValue<int>(out var int32) ? int32 : 0;
    }

    private static void SetPhase(JsonObject progress, string phase)
    {
        var current = progress["phase"]?.GetValue<string>();
        if (current is null || GetPhaseOrder(phase) >= GetPhaseOrder(current))
        {
            progress["phase"] = phase;
        }
    }

    private static int GetPhaseOrder(string phase) =>
        phase switch
        {
            "BarrierDelay" => 0,
            "Draining" => 1,
            "Reindexing" => 2,
            "Completing" => 3,
            _ => -1
        };
}
