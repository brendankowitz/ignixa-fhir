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
using Ignixa.Application.BackgroundOperations.Import;
using Ignixa.Application.BackgroundOperations.Jobs;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Workers;
using Ignixa.Application.Features.Conformance;
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

        // Terminology import bootstrap service (conditional)
        if (terminologyAutoImportEnabled)
        {
            services.AddHostedService<TerminologyImportBootstrapService>();
        }

        // TTL cleanup options
        services.Configure<TtlCleanupOptions>(configuration.GetSection(TtlCleanupOptions.SectionName));

        // Transaction watcher options (used by eternal orchestration)
        services.Configure<TransactionWatcherOptions>(configuration.GetSection(TransactionWatcherOptions.SectionName));

        services.AddOptions<ReindexOptions>()
            .Configure(options =>
            {
                configuration.GetSection(ReindexOptions.SectionName).Bind(options);
                options.BarrierDelay = configuration.GetValue<TimeSpan?>("Reindex:BarrierDelay")
                    ?? TimeSpan.FromSeconds(2 * configuration.GetValue("Conformance:SyncIntervalSeconds", 30));
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ReindexOptions>, ReindexOptionsValidator>();

        services.AddOptions<ConformanceTransitionOptions>()
            .Configure(options =>
            {
                configuration.GetSection(ConformanceTransitionOptions.SectionName).Bind(options);
                options.SyncIntervalSeconds = configuration.GetValue("Conformance:SyncIntervalSeconds", 30);
                options.MaxStaleness = configuration.GetValue<TimeSpan?>("Conformance:MaxStaleness")
                    ?? TimeSpan.FromSeconds(2 * options.SyncIntervalSeconds);
                options.TransitionGrace = configuration.GetValue<TimeSpan?>("Conformance:TransitionGrace")
                    ?? options.MaxStaleness + TimeSpan.FromSeconds(options.SyncIntervalSeconds);
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ConformanceTransitionOptions>, ConformanceTransitionOptionsValidator>();

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
        builder.RegisterType<ExportGroupResolver>().AsSelf().InstancePerDependency();
        builder.RegisterType<CreateExportJobHandler>()
            .As<IRequestHandler<CreateExportJobCommand, CreateExportJobResult>>()
            .InstancePerDependency();

        builder.RegisterType<CreateImportJobHandler>()
            .As<IRequestHandler<CreateImportJobCommand, CreateImportJobResult>>()
            .InstancePerDependency();

        builder.RegisterType<GetJobStatusHandler>()
            .As<IRequestHandler<GetJobStatusQuery, GetJobStatusResult>>()
            .InstancePerDependency();

        builder.RegisterType<CreateReindexJobHandler>()
            .As<IRequestHandler<CreateReindexJobCommand, CreateReindexJobResult>>()
            .InstancePerDependency();
        builder.RegisterType<GetReindexStatusHandler>()
            .As<IRequestHandler<GetReindexStatusQuery, ReindexStatusResult?>>()
            .InstancePerDependency();
        builder.RegisterType<GetReindexJobsHandler>()
            .As<IRequestHandler<GetReindexJobsQuery, IReadOnlyList<ReindexStatusResult>>>()
            .InstancePerDependency();
        builder.RegisterType<CancelReindexHandler>()
            .As<IRequestHandler<CancelReindexCommand, CancelReindexResult>>()
            .InstancePerDependency();
        builder.RegisterType<ReindexRangeProcessor>().AsSelf().InstancePerDependency();
        builder.RegisterType<ReindexLifecycleEventWriter>().AsSelf().SingleInstance();
        builder.RegisterType<ReindexJobUpdater>().AsSelf().SingleInstance();
        builder.RegisterType<ReindexJobReconciler>().AsSelf().SingleInstance();
        builder.RegisterType<ReindexAutomationStateStore>().AsSelf().SingleInstance();
        builder.RegisterType<ReindexStartupReconciler>().AsSelf().SingleInstance();
        builder.RegisterType<ReindexProgressReporter>().AsSelf().SingleInstance();
        builder.RegisterType<ReindexActivityHeartbeat>().AsSelf().SingleInstance();
        builder.RegisterType<ReindexCompletionHook>()
            .As<IReindexCompletionHook>()
            .SingleInstance();

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
