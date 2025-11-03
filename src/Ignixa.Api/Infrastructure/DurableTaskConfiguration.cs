using Azure.Core;
using Azure.Identity;
using Azure.Storage.Common;
using DurableTask.AzureStorage;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Export.Activities;
using Ignixa.Application.BackgroundOperations.Export.Orchestrations;
using Ignixa.Application.BackgroundOperations.Import.Orchestrations;
using Ignixa.DataLayer.FileSystem.DurableTask;
using ExportCompleteJobActivity = Ignixa.Application.BackgroundOperations.Export.Activities.CompleteJobActivity;
using ImportActivities = Ignixa.Application.BackgroundOperations.Import.Activities;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Configuration for DurableTask framework.
/// Sets up TaskHubClient and TaskHubWorker for background job processing.
/// </summary>
public static class DurableTaskConfiguration
{
    /// <summary>
    /// Registers DurableTask services with the service collection.
    /// Supports FileSystem and AzureStorage orchestration service providers.
    /// </summary>
    public static IServiceCollection AddDurableTask(this IServiceCollection services)
    {
        services.AddSingleton<IOrchestrationService>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var provider = configuration["DurableTask:Provider"] ?? "FileSystem";

            return provider.Equals("AzureStorage", StringComparison.OrdinalIgnoreCase)
                ? CreateAzureStorageOrchestrationService(sp, configuration)
                : CreateFileBasedOrchestrationService(sp, configuration);
        });

        // Register TaskHubWorker
        services.AddSingleton(sp =>
        {
            var orchestrationService = sp.GetRequiredService<IOrchestrationService>();
            var worker = new TaskHubWorker(orchestrationService);

            // Register orchestrations
            worker.AddTaskOrchestrations(typeof(ExportOrchestration));
            worker.AddTaskOrchestrations(typeof(ImportOrchestration));

            // Register Export activities with service provider for DI
            worker.AddTaskActivitiesFromInterface<SearchAndWriteChunkActivity>(sp);
            worker.AddTaskActivitiesFromInterface<ExportCompleteJobActivity>(sp);

            // Register Import activities with service provider for DI
            worker.AddTaskActivitiesFromInterface<ImportActivities.ValidateFileActivity>(sp);
            worker.AddTaskActivitiesFromInterface<ImportActivities.StreamingImportFileActivity>(sp);
            worker.AddTaskActivitiesFromInterface<ImportActivities.UpdateProgressActivity>(sp);
            worker.AddTaskActivitiesFromInterface<ImportActivities.CompleteJobActivity>(sp);

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
        var activityCreator = new ServiceProviderObjectCreator<TActivity>(serviceProvider);
        worker.AddTaskActivities(activityCreator.Create());
        return worker;
    }
}

/// <summary>
/// Object creator that resolves activities from the service provider.
/// Enables dependency injection for DurableTask activities.
/// </summary>
internal class ServiceProviderObjectCreator<T> : ObjectCreator<T>
    where T : class
{
    private readonly IServiceProvider _serviceProvider;

    public ServiceProviderObjectCreator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public override T Create()
    {
        return _serviceProvider.GetRequiredService<T>();
    }
}
