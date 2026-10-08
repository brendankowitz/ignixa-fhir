using System.Text.Json.Nodes;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class ReindexProgressReporter(ReindexJobUpdater jobs)
{
    internal static void InitializeBarrierDelay(
        BackgroundJob<ReindexJobDefinition> job,
        IReadOnlyList<int> tenantIds,
        IReadOnlyList<string> ignoredLifecycleEvents)
    {
        job.Status = "Running";
        job.StartDate ??= DateTimeOffset.UtcNow;
        var progress = new JsonObject
        {
            ["phase"] = "BarrierDelay",
            ["ignoredLifecycleEvents"] = new JsonArray(
                ignoredLifecycleEvents
                    .Select(value => (JsonNode?)JsonValue.Create(value))
                    .ToArray()),
            ["notCovered"] = job.Progress?["notCovered"]?.DeepClone() ??
                new JsonArray(
                    job.Definition.SearchParameters
                        .Where(target => !IsFullyCovered(target))
                        .Select(target => (JsonNode?)JsonValue.Create(target.Canonical))
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
        jobs.UpdateProgressAsync(jobId, _ => { }, cancellationToken);

    public Task<bool> ReportAsync(
        PersistReindexProgressInput input,
        CancellationToken cancellationToken) =>
        jobs.UpdateProgressAsync(
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

    private static bool IsFullyCovered(ReindexParameterDefinition target) =>
        target.AffectedResourceTypes.Count == target.ScheduledResourceTypes.Count &&
        target.AffectedResourceTypes.All(type =>
            target.ScheduledResourceTypes.Contains(type, StringComparer.OrdinalIgnoreCase));

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
