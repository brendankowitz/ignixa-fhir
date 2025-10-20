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
    /// </summary>
    public static IServiceCollection AddDurableTask(this IServiceCollection services)
    {
        // Register FileBasedOrchestrationService (file-based persistence for development/prototype)
        // Production will use SqlOrchestrationService or AzureStorageOrchestrationService
        services.AddSingleton<IOrchestrationService>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
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
            worker.AddTaskActivitiesFromInterface<ImportActivities.DownloadAndParseActivity>(sp);
            worker.AddTaskActivitiesFromInterface<ImportActivities.ImportBatchActivity>(sp);
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
