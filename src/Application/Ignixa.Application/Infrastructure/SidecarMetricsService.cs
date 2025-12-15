// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Ignixa.Domain.Abstractions;
using Ignixa.Sidecar.Metrics;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Metrics service implementation that forwards metrics to sidecar gRPC service.
/// Fire-and-forget: Does not block request processing.
/// </summary>
public class SidecarMetricsService(
    MetricsService.MetricsServiceClient client,
    ILogger<SidecarMetricsService> logger) : IMetricsService
{
    public ValueTask RecordMetricAsync(FhirOperationMetrics metrics, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        // Fire-and-forget: Queue for async processing
        _ = RecordMetricInternalAsync(metrics, cancellationToken);

        return ValueTask.CompletedTask;
    }

    private async Task RecordMetricInternalAsync(FhirOperationMetrics metrics, CancellationToken cancellationToken)
    {
        try
        {
            var request = new FhirMetricsRequest
            {
                Timestamp = Timestamp.FromDateTimeOffset(metrics.Timestamp),
                CorrelationId = metrics.CorrelationId,
                OperationId = metrics.OperationId,
                TenantId = metrics.TenantId.ToString(),
                ResourceType = metrics.ResourceType ?? string.Empty,
                ResourceId = metrics.ResourceId ?? string.Empty,
                FhirVersion = metrics.FhirVersion,
                HttpMethod = metrics.HttpMethod,
                FhirOperation = metrics.FhirOperation,
                HttpStatusCode = metrics.StatusCode,
                Success = metrics.Success,
                RequestSizeBytes = metrics.RequestSizeBytes,
                ResponseSizeBytes = metrics.ResponseSizeBytes,
                DurationMilliseconds = metrics.DurationMilliseconds,
                ResourceCount = metrics.ResourceCount ?? 0,
                TotalMatches = metrics.TotalMatches ?? 0
            };

            if (metrics.CustomProperties != null)
            {
                foreach (var (key, value) in metrics.CustomProperties)
                {
                    request.CustomProperties.Add(key, value);
                }
            }

            await client.RecordMetricAsync(request, cancellationToken: cancellationToken);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            logger.LogError(ex, "Metrics sidecar unavailable - metric lost");
            // Don't rethrow - metrics failures should not block requests
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record metric");
        }
    }
}
