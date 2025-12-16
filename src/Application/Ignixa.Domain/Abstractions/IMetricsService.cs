namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Service for recording FHIR operation metrics.
/// Implementations can log locally, send to Application Insights, or forward to billing systems.
/// </summary>
public interface IMetricsService
{
    /// <summary>
    /// Records metrics for a FHIR operation.
    /// Fire-and-forget - does not block request processing.
    /// </summary>
    /// <param name="metrics">The metrics data to record</param>
    /// <param name="cancellationToken">Cancellation token</param>
    ValueTask RecordMetricAsync(FhirOperationMetrics metrics, CancellationToken cancellationToken = default);
}

/// <summary>
/// Metrics data for a single FHIR operation.
/// </summary>
public sealed record FhirOperationMetrics
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string CorrelationId { get; init; }
    public required string OperationId { get; init; }
    public required int TenantId { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public required string FhirVersion { get; init; }
    public required string HttpMethod { get; init; }
    public required string FhirOperation { get; init; }
    public required int StatusCode { get; init; }
    public required bool Success { get; init; }
    public required long RequestSizeBytes { get; init; }
    public required long ResponseSizeBytes { get; init; }
    public required long DurationMilliseconds { get; init; }
    public int? ResourceCount { get; init; }
    public int? TotalMatches { get; init; }
    public Dictionary<string, string>? CustomProperties { get; init; }
}
