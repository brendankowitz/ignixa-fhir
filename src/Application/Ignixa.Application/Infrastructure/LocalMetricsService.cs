using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Local metrics service using structured logging.
/// Replace with custom implementation for Application Insights, billing systems, etc.
/// </summary>
public partial class LocalMetricsService(ILogger<LocalMetricsService> logger) : IMetricsService
{
    public ValueTask RecordMetricAsync(FhirOperationMetrics metrics, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        LogMetric(
            logger,
            metrics.TenantId,
            metrics.FhirOperation,
            metrics.ResourceType ?? "(none)",
            metrics.ResourceId ?? "(none)",
            metrics.StatusCode,
            metrics.DurationMilliseconds,
            metrics.RequestSizeBytes,
            metrics.ResponseSizeBytes);

        return ValueTask.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "METRICS: Tenant={TenantId}, Operation={Operation}, Resource={ResourceType}/{ResourceId}, Status={StatusCode}, Duration={DurationMs}ms, Request={RequestBytes}B, Response={ResponseBytes}B")]
    private static partial void LogMetric(
        ILogger logger,
        int tenantId,
        string operation,
        string resourceType,
        string resourceId,
        int statusCode,
        long durationMs,
        long requestBytes,
        long responseBytes);
}
