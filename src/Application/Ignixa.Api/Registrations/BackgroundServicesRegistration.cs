// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Api.BackgroundServices;
using Ignixa.Api.Configuration;
using Ignixa.Api.Infrastructure;
using Ignixa.Api.Services;
using Ignixa.Application.BackgroundOperations.Export;
using Ignixa.Application.BackgroundOperations.Jobs;
using Medino;

namespace Ignixa.Api.Registrations;

/// <summary>
/// Registers background services including DurableTask framework, hosted services,
/// and background job handlers.
/// </summary>
public static class BackgroundServicesRegistration
{
    /// <summary>
    /// Adds background services to the service collection.
    /// </summary>
    public static IServiceCollection AddIgnixaBackgroundServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var terminologyAutoImportEnabled = configuration.GetValue<bool>("Experimental:Features:Terminology:EnableAutoImport", false);

        // Index loader service
        services.AddHostedService<IndexLoaderService>();

        // Tenant package preload service
        services.AddHostedService<TenantPackagePreloadService>();

        // SQL reference data preload handler
        services.AddSingleton<SqlReferenceDataPreloadHandler>();

        // Terminology import bootstrap service (conditional)
        if (terminologyAutoImportEnabled)
        {
            services.AddHostedService<TerminologyImportBootstrapService>();
        }

        // TTL cleanup options
        services.Configure<TtlCleanupOptions>(configuration.GetSection(TtlCleanupOptions.SectionName));

        // Transaction watcher options (used by eternal orchestration)
        services.Configure<TransactionWatcherOptions>(configuration.GetSection(TransactionWatcherOptions.SectionName));

        // Eternal orchestration starter (starts all periodic DurableTask orchestrations)
        services.AddHostedService<EternalOrchestrationStarter>();

        // DurableTask framework
        services.AddDurableTask();

        return services;
    }

    /// <summary>
    /// Registers background job handlers in the Autofac container.
    /// </summary>
    public static ContainerBuilder RegisterBackgroundJobHandlers(this ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Export job handlers
        builder.RegisterType<CreateExportJobHandler>()
            .As<IRequestHandler<CreateExportJobCommand, CreateExportJobResult>>()
            .InstancePerDependency();

        builder.RegisterType<GetJobStatusHandler>()
            .As<IRequestHandler<GetJobStatusQuery, GetJobStatusResult>>()
            .InstancePerDependency();

        return builder;
    }

    /// <summary>
    /// Adds MCP (Model Context Protocol) server services.
    /// </summary>
    public static IServiceCollection AddIgnixaMcpServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        var mcpEnabled = configuration.GetValue<bool>("Experimental:Features:Mcp:Enabled", true);
        if (mcpEnabled)
        {
            services
                .AddMcpServer()
                .WithHttpTransport()
                .WithToolsFromAssembly(typeof(Ignixa.Application.Features.Experimental.Mcp.Tools.DiagnosticTool).Assembly)
                .WithToolsFromAssembly(typeof(Ignixa.Application.BackgroundOperations.JobManagement.GetJobStatusTool).Assembly);
        }

        return services;
    }
}
