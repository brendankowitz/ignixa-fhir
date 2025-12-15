// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Grpc.Net.Client;
using Ignixa.Application.Infrastructure;
using Ignixa.Sidecar.Audit;
using Ignixa.Sidecar.Metrics;
using Ignixa.Sidecar.Rbac;

namespace Ignixa.Api.Registrations;

/// <summary>
/// Registers gRPC client services for sidecar integration.
/// Only invoked when Sidecar.Enabled = true.
/// </summary>
public static class GrpcServicesRegistration
{
    /// <summary>
    /// Adds gRPC clients for sidecar services.
    /// </summary>
    public static IServiceCollection AddSidecarGrpcClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var sidecarOptions = new SidecarOptions();
        configuration.GetSection(SidecarOptions.SectionName).Bind(sidecarOptions);

        if (!sidecarOptions.Enabled)
        {
            // Sidecar disabled - skip gRPC client registration
            return services;
        }

        // Register gRPC clients with proper error handling
        var timeout = TimeSpan.FromSeconds(sidecarOptions.TimeoutSeconds);

        // Audit Service Client
        services.AddGrpcClient<AuditService.AuditServiceClient>(o =>
        {
            o.Address = new Uri(sidecarOptions.AuditServiceUrl);
        })
        .ConfigureChannel(options =>
        {
            options.HttpHandler = CreateHttpHandler(timeout);
        });

        // RBAC Service Client
        services.AddGrpcClient<RbacService.RbacServiceClient>(o =>
        {
            o.Address = new Uri(sidecarOptions.RbacServiceUrl);
        })
        .ConfigureChannel(options =>
        {
            options.HttpHandler = CreateHttpHandler(timeout);
        });

        // Metrics Service Client
        services.AddGrpcClient<MetricsService.MetricsServiceClient>(o =>
        {
            o.Address = new Uri(sidecarOptions.MetricsServiceUrl);
        })
        .ConfigureChannel(options =>
        {
            options.HttpHandler = CreateHttpHandler(timeout);
        });

        return services;
    }

    private static SocketsHttpHandler CreateHttpHandler(TimeSpan timeout)
    {
        return new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = timeout
        };
    }
}
