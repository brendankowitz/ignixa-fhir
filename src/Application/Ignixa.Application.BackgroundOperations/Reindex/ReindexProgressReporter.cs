using System.Text.Json.Nodes;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class ReindexProgressReporter(ReindexJobUpdater jobs)
{
    public Task ReportBarrierDelayAsync(
        string jobId,
        IReadOnlyList<int> tenantIds,
        IReadOnlyList<string> ignoredLifecycleEvents,
        CancellationToken cancellationToken) =>
        jobs.UpdateAsync(
            jobId,
            job =>
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
            },
            cancellationToken);

    public Task ReportBarrierAsync(
        string jobId,
        RaiseBarrierOutput output,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            jobId,
            "Draining",
            output.TenantId,
            tenant =>
            {
                tenant["cutoffTransactionId"] = output.CutoffTransactionId;
                tenant["cutoffSurrogateId"] = output.CutoffSurrogateId;
                tenant["status"] = "Draining";
            },
            cancellationToken);

    public Task ReportDrainAsync(
        string jobId,
        AwaitDrainOutput output,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            jobId,
            output.IsDrained ? "Reindexing" : "Draining",
            output.TenantId,
            tenant =>
            {
                tenant["visibleWatermark"] = output.VisibleWatermark;
                tenant["status"] = output.IsDrained ? "Reindexing" : "Draining";
            },
            cancellationToken);

    public Task ReportPlanAsync(
        PlanReindexInput input,
        PlanReindexOutput output,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            input.JobId,
            "Reindexing",
            input.TenantId,
            tenant =>
            {
                tenant["status"] = "Reindexing";
                tenant["resourcesToReindex"] =
                    GetInt64(tenant, "resourcesToReindex") +
                    output.Ranges.Sum(range => range.ResourceCount);
                tenant["plannerCursor"] = output.NextStartAfter;
            },
            cancellationToken);

    public Task ReportRangeAsync(
        ReindexRangeInput input,
        ReindexRangeOutput output,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            input.JobId,
            "Reindexing",
            input.TenantId,
            tenant =>
            {
                tenant["status"] = "Reindexing";
                tenant["resourcesRead"] = GetInt64(tenant, "resourcesRead") + output.ResourcesRead;
                tenant["resourcesReindexed"] =
                    GetInt64(tenant, "resourcesReindexed") + output.ResourcesReindexed;
                tenant["conflicts"] = GetInt64(tenant, "conflicts") + output.Conflicts;
                tenant["failedResources"] =
                    GetInt64(tenant, "failedResources") + output.FailedResourceCount;
            },
            cancellationToken);

    public Task ReportCompletingAsync(string jobId, CancellationToken cancellationToken) =>
        jobs.UpdateAsync(
            jobId,
            job =>
            {
                var progress = EnsureProgress(job.Progress);
                SetPhase(progress, "Completing");
                Recalculate(progress, job.Status);
                job.Progress = progress;
            },
            cancellationToken);

    private Task UpdateAsync(
        string jobId,
        string phase,
        int tenantId,
        Action<JsonObject> updateTenant,
        CancellationToken cancellationToken) =>
        jobs.UpdateAsync(
            jobId,
            job =>
            {
                var progress = EnsureProgress(job.Progress);
                SetPhase(progress, phase);
                var tenant = GetOrAddTenant(progress, tenantId);
                updateTenant(tenant);
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
