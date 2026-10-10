using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

/// <summary>
/// A reindex job's reported progress: the one shape written to <c>BackgroundJob.Progress</c> (camelCase JSON)
/// and read back for the status response. Totals derive from the tenants, so nothing here can disagree.
/// </summary>
public sealed record ReindexProgress(ReindexPhase Phase)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public IReadOnlyList<ReindexTenantProgress> Tenants { get; init; } = [];

    public IReadOnlyList<string> IgnoredLifecycleEvents { get; init; } = [];

    public string? CancellationReason { get; init; }

    public static ReindexProgress Create(IReadOnlyList<int> tenantIds) =>
        new(ReindexPhase.BarrierDelay)
        {
            Tenants = tenantIds.Select(ReindexTenantProgress.Create).ToArray()
        };

    [Newtonsoft.Json.JsonIgnore]
    [JsonIgnore]
    public long TotalResourcesToReindex => Tenants.Sum(tenant => tenant.ResourcesToReindex);

    [Newtonsoft.Json.JsonIgnore]
    [JsonIgnore]
    public long ResourcesSuccessfullyReindexed => Tenants.Sum(tenant => tenant.ResourcesReindexed);

    [Newtonsoft.Json.JsonIgnore]
    [JsonIgnore]
    public long Conflicts => Tenants.Sum(tenant => tenant.Conflicts);

    [Newtonsoft.Json.JsonIgnore]
    [JsonIgnore]
    public IReadOnlyList<ReindexFailedResource> FailedResources =>
        Tenants.SelectMany(tenant => tenant.FailedResources)
            .Take(ReindexTenantProgress.FailedResourceSampleSize)
            .ToArray();

    /// <summary>The percentage reported as <c>progress</c>: 100 only once the job is Completed.</summary>
    public double PercentComplete(ReindexJobStatus status) =>
        status == ReindexJobStatus.Completed
            ? 100
            : TotalResourcesToReindex == 0
                ? 0
                : Math.Min(99.9, ResourcesSuccessfullyReindexed * 100.0 / TotalResourcesToReindex);

    public JsonNode ToJson() => JsonSerializer.SerializeToNode(this, SerializerOptions)!;

    public static ReindexProgress? FromJson(JsonNode? json) =>
        json?.Deserialize<ReindexProgress>(SerializerOptions);
}
