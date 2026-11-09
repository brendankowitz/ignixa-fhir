namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

/// <summary>
/// One tenant's position in a reindex job, reported as the tenant's <c>status</c>. The orchestration moves a
/// tenant forward through the first three; Completed and Failed are final.
/// </summary>
[Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum ReindexTenantStatus
{
    BarrierDelay,
    Draining,
    Reindexing,
    Completed,
    Failed
}
