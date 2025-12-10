// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Autofac.Extensions.DependencyInjection;
using Ignixa.Api.Extensions;
using Ignixa.Api.Infrastructure;
using Ignixa.Api.Services;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;

var builder = WebApplication.CreateBuilder(args);

// Configure Autofac as the service provider factory
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

// Add all Ignixa services to the service collection
builder.Services.AddIgnixaApi(builder.Configuration, builder.Environment);

// Configure Autofac container with all Ignixa registrations
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    containerBuilder.RegisterIgnixaServices(builder.Configuration, builder.Environment.EnvironmentName);
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseIgnixaApi(builder.Configuration, builder.Environment);

// Map all Ignixa FHIR endpoints
app.MapIgnixaEndpoints(builder.Configuration);

// Log startup information
LogStartupInfo(app, builder);

// Validate multi-tenancy configuration
await ValidateMultiTenancyConfigurationAsync(app);

// Initialize all tenant databases
await InitializeDatabasesAsync(app);

await app.RunAsync();

// Local functions for startup tasks - these must come before type declarations
static void LogStartupInfo(WebApplication app, WebApplicationBuilder builder)
{
    app.Logger.LogInformation("Ignixa FHIR starting...");
    app.Logger.LogInformation("FHIR data directory: {BaseDirectory}",
        builder.Configuration["FhirRepository:BaseDirectory"]
        ?? Path.Combine(Directory.GetCurrentDirectory(), "fhir-data"));
}

static async Task ValidateMultiTenancyConfigurationAsync(WebApplication app)
{
    var configStore = app.Services.GetRequiredService<ITenantConfigurationStore>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    logger.LogInformation("===== FHIR Server Multi-Tenancy Configuration =====");
    logger.LogInformation(
        "Mode: {Mode} ({Description})",
        configStore.Mode,
        configStore.Mode == TenantMode.Isolated
            ? "Multiple separate customers with isolated data stores"
            : "Single customer with horizontal sharding");

    var tenants = await configStore.GetAllTenantsAsync();
    logger.LogInformation("Active Tenants: {Count}", tenants.Count);

    foreach (var tenant in tenants)
    {
        logger.LogInformation(
            "  - Tenant {TenantId}: {DisplayName} (FHIR {FhirVersion}, Storage: {StorageType})",
            tenant.TenantId,
            tenant.DisplayName,
            tenant.FhirVersion,
            tenant.Storage.Type);
    }

    if (configStore.Mode == TenantMode.Distributed)
    {
        logger.LogWarning(
            "WARNING: Distributed mode is configured but not yet implemented (Phase 20.2+). " +
            "The system will throw NotSupportedException if Distributed features are accessed. " +
            "For production use, set Tenants:Mode to 'Isolated' in appsettings.json.");
    }

    var partitionStrategy = app.Services.GetRequiredService<IPartitionStrategy>();
    var executionStrategy = app.Services.GetRequiredService<IQueryExecutionStrategy>();

    logger.LogInformation(
        "Registered Strategies: IPartitionStrategy={PartitionStrategy}, IQueryExecutionStrategy={ExecutionStrategy}",
        partitionStrategy.GetType().Name,
        executionStrategy.GetType().Name);

    if (configStore.Mode == TenantMode.Isolated)
    {
        if (partitionStrategy is not IsolatedModePartitionStrategy)
        {
            logger.LogError(
                "Configuration Error: Mode is Isolated but IPartitionStrategy is {ActualType}. " +
                "Expected: IsolatedModePartitionStrategy",
                partitionStrategy.GetType().Name);
            throw new InvalidOperationException(
                "Configuration mismatch: Mode is Isolated but wrong partition strategy registered");
        }

        if (executionStrategy is not PassthroughExecutionStrategy)
        {
            logger.LogError(
                "Configuration Error: Mode is Isolated but IQueryExecutionStrategy is {ActualType}. " +
                "Expected: PassthroughExecutionStrategy",
                executionStrategy.GetType().Name);
            throw new InvalidOperationException(
                "Configuration mismatch: Mode is Isolated but wrong execution strategy registered");
        }

        logger.LogInformation("Isolation mode validation passed");
    }

    logger.LogInformation("===================================================");
}

static async Task InitializeDatabasesAsync(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var configStore = app.Services.GetRequiredService<ITenantConfigurationStore>();
    var repositoryFactory = app.Services.GetRequiredService<IFhirRepositoryFactory>();
    var startupTiming = app.Services.GetRequiredService<StartupTimingDiagnostics>();

    logger.LogInformation("===== Database Initialization =====");

    // Include system partition (tenant 0) which GetAllTenantsAsync excludes
    var systemPartition = await configStore.GetTenantConfigurationAsync(SystemConstants.SystemPartitionId);
    var tenants = await configStore.GetAllTenantsAsync();
    var allTenantsToInit = new List<TenantConfiguration>();
    if (systemPartition?.IsActive == true)
    {
        allTenantsToInit.Add(systemPartition);
    }
    allTenantsToInit.AddRange(tenants);

    foreach (var tenant in allTenantsToInit)
    {
        try
        {
            using (startupTiming.StartPhase($"Database.Init.Tenant{tenant.TenantId}"))
            {
                logger.LogInformation(
                    "Initializing database for tenant {TenantId} ({DisplayName})...",
                    tenant.TenantId,
                    tenant.DisplayName);

                var repository = await repositoryFactory.GetRepositoryAsync(tenant.TenantId);

                logger.LogInformation("Database initialized for tenant {TenantId}", tenant.TenantId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to initialize database for tenant {TenantId} ({DisplayName}). Error: {Message}",
                tenant.TenantId,
                tenant.DisplayName,
                ex.Message);
            throw;
        }
    }

    logger.LogInformation("===== All Databases Initialized =====");

    startupTiming.LogSummary();
}

// Explicit partial class to make Program public for integration testing
public partial class Program { }
