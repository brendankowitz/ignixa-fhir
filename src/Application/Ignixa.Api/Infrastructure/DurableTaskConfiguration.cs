using Azure.Core;
using Azure.Identity;
using Azure.Storage.Common;
using DurableTask.AzureStorage;
using DurableTask.Core;
using DurableTask.SqlServer;
using Ignixa.Application.BackgroundOperations.Export.Activities;
using Ignixa.Application.BackgroundOperations.Export.Orchestrations;
using Ignixa.Application.BackgroundOperations.Import.Orchestrations;
using Ignixa.Application.BackgroundOperations.Terminology.Orchestrations;
using Ignixa.DataLayer.FileSystem.DurableTask;
using ExportCompleteJobActivity = Ignixa.Application.BackgroundOperations.Export.Activities.CompleteJobActivity;
using ImportActivities = Ignixa.Application.BackgroundOperations.Import.Activities;
using TerminologyActivities = Ignixa.Application.BackgroundOperations.Terminology.Activities;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Configuration for DurableTask framework.
/// Sets up TaskHubClient and TaskHubWorker for background job processing.
/// </summary>
public static class DurableTaskConfiguration
{
    /// <summary>
    /// Registers DurableTask services with the service collection.
    /// Supports FileSystem, SqlServer, and AzureStorage orchestration service providers.
    /// </summary>
    public static IServiceCollection AddDurableTask(this IServiceCollection services)
    {
        services.AddSingleton<IOrchestrationService>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var provider = configuration["DurableTask:Provider"] ?? "SqlServer";

            return provider.ToUpperInvariant() switch
            {
                "SQLSERVER" => CreateSqlServerOrchestrationService(sp, configuration),
                "AZURESTORAGE" => CreateAzureStorageOrchestrationService(sp, configuration),
                _ => CreateFileBasedOrchestrationService(sp, configuration),
            };
        });

        // Register TaskHubWorker
        services.AddSingleton(sp =>
        {
            var orchestrationService = sp.GetRequiredService<IOrchestrationService>();
            var worker = new TaskHubWorker(orchestrationService);

            // Register orchestrations
            worker.AddTaskOrchestrations(typeof(ExportOrchestration));
            worker.AddTaskOrchestrations(typeof(ImportOrchestration));
            worker.AddTaskOrchestrations(typeof(TerminologyImportOrchestration));

            // Register Export activities with service provider for DI
            worker.AddTaskActivitiesFromInterface<GetExportRangesActivity>(sp);
            worker.AddTaskActivitiesFromInterface<ExportWorkerActivity>(sp);
            worker.AddTaskActivitiesFromInterface<ExportCompleteJobActivity>(sp);

            // Register Import activities with service provider for DI
            worker.AddTaskActivitiesFromInterface<ImportActivities.ValidateFileActivity>(sp);
            worker.AddTaskActivitiesFromInterface<ImportActivities.StreamingImportFileActivity>(sp);
            worker.AddTaskActivitiesFromInterface<ImportActivities.UpdateProgressActivity>(sp);
            worker.AddTaskActivitiesFromInterface<ImportActivities.CompleteJobActivity>(sp);

            // Register Terminology activities with service provider for DI
            worker.AddTaskActivitiesFromInterface<TerminologyActivities.ImportTerminologyResourceActivity>(sp);

            return worker;
        });

        // Register TaskHubClient
        services.AddSingleton(sp =>
        {
            var orchestrationService = sp.GetRequiredService<IOrchestrationService>();
            return new TaskHubClient(orchestrationService as IOrchestrationServiceClient
                ?? throw new InvalidOperationException("IOrchestrationService must also implement IOrchestrationServiceClient"));
        });

        // Register hosted service to start/stop worker
        services.AddHostedService<DurableTaskHostedService>();

        return services;
    }

    /// <summary>
    /// Creates a file-based orchestration service for development/testing.
    /// </summary>
    private static IOrchestrationService CreateFileBasedOrchestrationService(IServiceProvider sp, IConfiguration configuration)
    {
        var baseDir = configuration["FhirRepository:BaseDirectory"] ?? "fhir-data";
        var logger = sp.GetRequiredService<ILogger<FileBasedOrchestrationService>>();
        var innerLogger = sp.GetRequiredService<ILogger<InMemoryOrchestrationService>>();

        return new FileBasedOrchestrationService(
            new FileBasedOrchestrationServiceOptions
            {
                BaseDirectory = baseDir,
                WorkItemLockTimeout = TimeSpan.FromMinutes(5),
                StateFlushInterval = TimeSpan.FromSeconds(1),
            },
            logger,
            innerLogger);
    }

    /// <summary>
    /// Creates a SQL Server-based orchestration service for production use.
    /// Uses SQL Server for orchestration state with no additional dependencies beyond existing SQL Server.
    /// Schema is automatically initialized via CreateIfNotExistsAsync in the hosted service.
    /// </summary>
    private static IOrchestrationService CreateSqlServerOrchestrationService(IServiceProvider sp, IConfiguration configuration)
    {
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("DurableTask.SqlServer");

        // Get connection string in order of preference:
        // 1. Explicit DurableTask:SqlServer:ConnectionString (highest priority)
        // 2. First active tenant with SQL storage (uses same database as FHIR resources)
        var connectionString = configuration["DurableTask:SqlServer:ConnectionString"];

        if (string.IsNullOrEmpty(connectionString))
        {
            // Find the first active tenant with a SQL connection string
            connectionString = GetFirstActiveTenantConnectionString(configuration);
        }

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "DurableTask SqlServer connection string not found. Configure either 'DurableTask:SqlServer:ConnectionString' " +
                "or ensure at least one active tenant has a SQL Server storage connection string configured.");
        }

        var taskHubName = configuration["DurableTask:SqlServer:TaskHubName"] ?? "ignixa";

        logger.LogInformation(
            "Initializing DurableTask with SQL Server backend (TaskHub: {TaskHubName})",
            taskHubName);

        var settings = new SqlOrchestrationServiceSettings(connectionString, taskHubName);

        return new SqlOrchestrationService(settings);
    }

    /// <summary>
    /// Gets the connection string from the first active tenant with SQL storage.
    /// </summary>
    private static string? GetFirstActiveTenantConnectionString(IConfiguration configuration)
    {
        var tenantsSection = configuration.GetSection("Tenants:Configurations");
        if (!tenantsSection.Exists())
        {
            return null;
        }

        // Iterate through tenant configurations to find the first active tenant with SQL storage
        // (excluding system partitions which only store transaction IDs)
        foreach (var tenantSection in tenantsSection.GetChildren())
        {
            var isActive = tenantSection.GetValue<bool>("IsActive", false);
            var isSystemPartition = tenantSection.GetValue<bool>("IsSystemPartition", false);
            var storageType = tenantSection["Storage:Type"];
            var connectionString = tenantSection["Storage:ConnectionString"];

            // Skip system partitions and inactive tenants
            if (!isActive || isSystemPartition)
            {
                continue;
            }

            // Check if this tenant uses SQL storage with a valid connection string
            if (storageType?.Equals("SqlEntityFramework", StringComparison.OrdinalIgnoreCase) == true
                && !string.IsNullOrEmpty(connectionString))
            {
                return connectionString;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates an Azure Storage-based orchestration service for cloud deployment.
    /// Uses Azure Table Storage for orchestration state and Azure Blob Storage for instance data.
    /// Supports both connection string and Managed Identity authentication.
    /// </summary>
    private static IOrchestrationService CreateAzureStorageOrchestrationService(IServiceProvider sp, IConfiguration configuration)
    {
        var taskHubName = configuration["DurableTask:AzureStorage:TaskHubName"] ?? "ignixa";
        var useManagedIdentity = configuration.GetValue<bool>("DurableTask:AzureStorage:UseManagedIdentity", false);
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("DurableTask.AzureStorage");

        StorageAccountClientProvider storageProvider;

        if (useManagedIdentity)
        {
            var storageAccountName = configuration["DurableTask:AzureStorage:StorageAccountName"];
            if (string.IsNullOrEmpty(storageAccountName))
            {
                throw new InvalidOperationException(
                    "DurableTask:AzureStorage:StorageAccountName is required when using Managed Identity");
            }

            logger.LogInformation(
                "Initializing DurableTask with Managed Identity for storage account: {AccountName}",
                storageAccountName);

            // Use ManagedIdentityCredential for production (secure, MI-only)
            // Use DefaultAzureCredential only for local development (flexible: MI > CLI > VS > Env)
            var isDevelopment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
            var credential = isDevelopment
                ? new DefaultAzureCredential() as TokenCredential
                : new ManagedIdentityCredential();

            storageProvider = new StorageAccountClientProvider(storageAccountName, credential);
        }
        else
        {
            var connectionString = configuration["DurableTask:AzureStorage:ConnectionString"];
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException(
                    "DurableTask:AzureStorage:ConnectionString is required when UseManagedIdentity is false");
            }

            logger.LogDebug("Initializing DurableTask with connection string");
            storageProvider = new StorageAccountClientProvider(connectionString);
        }

        // Create orchestration service with Azure Storage backend
        var settings = new AzureStorageOrchestrationServiceSettings
        {
            StorageAccountClientProvider = storageProvider,
            TaskHubName = taskHubName,
            PartitionCount = 4,
            UseAppLease = true,
        };

        return new AzureStorageOrchestrationService(settings);
    }
}

/// <summary>
/// Extension methods for registering activities with dependency injection.
/// </summary>
internal static class TaskHubWorkerExtensions
{
    public static TaskHubWorker AddTaskActivitiesFromInterface<TActivity>(
        this TaskHubWorker worker,
        IServiceProvider serviceProvider)
        where TActivity : TaskActivity
    {
        // CRITICAL: Use AddTaskActivities overload that accepts ObjectCreator<TaskActivity>
        // This ensures a new activity instance is created for each execution with fresh dependencies
        var activityCreator = new ServiceProviderObjectCreator<TaskActivity>(serviceProvider, typeof(TActivity));
        worker.AddTaskActivities(activityCreator);
        return worker;
    }
}

/// <summary>
/// Object creator that resolves activities from the service provider.
/// Enables dependency injection for DurableTask activities.
/// Supports both generic type parameter and explicit type specification for flexible registration.
/// </summary>
internal class ServiceProviderObjectCreator<T> : ObjectCreator<T>
    where T : class
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Type? _concreteType;

    public ServiceProviderObjectCreator(IServiceProvider serviceProvider, Type? concreteType = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _concreteType = concreteType;

        // Set Name and Version for DurableTask's activity registration
        // Name must be unique to avoid "Duplicate entry detected" errors
        Name = GetActivityName(concreteType ?? typeof(T));
        Version = string.Empty;
    }

    public override T Create()
    {
        // If explicit type provided, resolve that type and cast to T
        // Otherwise, resolve T directly
        if (_concreteType != null)
        {
            return (T)_serviceProvider.GetRequiredService(_concreteType);
        }

        return _serviceProvider.GetRequiredService<T>();
    }

    private static string GetActivityName(Type type)
    {
        // Use the full type name for uniqueness (e.g., "Ignixa.Application.BackgroundOperations.Export.Activities.SearchAndWriteChunkActivity")
        // DurableTask uses this name to route work items to the correct activity implementation
        return type.FullName ?? type.Name;
    }
}
