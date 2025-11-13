using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Export.Activities;
using Ignixa.Application.BackgroundOperations.Export.Models;
using Ignixa.Domain.Constants;

namespace Ignixa.Application.BackgroundOperations.Export.Orchestrations;

/// <summary>
/// Durable Task orchestration for FHIR bulk export operations.
/// Implements high-performance partition-based export:
/// 1. Determines which resource types to export
/// 2. For EACH type: calls GetExportRangesActivity to partition into surrogate ID ranges
/// 3. For EACH partition: queues ExportWorkerActivity to stream range to file
/// 4. Waits for all workers to complete in parallel
/// 5. Returns aggregated results
///
/// This design achieves >10K resources/sec by:
/// - Eliminating pagination (no continuation tokens)
/// - Streaming directly from DB to file (no intermediate buffering)
/// - Parallel execution of 24-48 worker activities (6 types × 4-8 ranges each)
/// - Zero-copy serialization (raw bytes from SearchEntryResult)
/// </summary>
public class ExportOrchestration : TaskOrchestration<ExportCoordinatorOutput, ExportCoordinatorInput>
{
    /// <summary>
    /// Valid range for NumberOfRangesPerType configuration.
    /// Minimum: 1 (no parallelism per type), Maximum: 16 (high parallelism but DurableTask overhead).
    /// </summary>
    private const int MinRangesPerType = 1;
    private const int MaxRangesPerType = 16;

    public override async Task<ExportCoordinatorOutput> RunTask(
        OrchestrationContext context,
        ExportCoordinatorInput input)
    {
        var workerResults = new List<ExportWorkerOutput>();
        long totalResourcesExported = 0;
        long totalBytesWritten = 0;

        try
        {
            // Validate NumberOfRangesPerType - fail fast if out of range
            if (input.NumberOfRangesPerType < MinRangesPerType || input.NumberOfRangesPerType > MaxRangesPerType)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input),
                    input.NumberOfRangesPerType,
                    $"NumberOfRangesPerType must be between {MinRangesPerType} and {MaxRangesPerType}. " +
                    $"Lower values reduce parallelism but decrease DurableTask overhead. " +
                    $"Higher values increase parallelism but may overwhelm the task queue.");
            }

            var rangesPerType = input.NumberOfRangesPerType;

            // Determine which resource types to export
            var resourceTypes = input.ResourceTypes.Any()
                ? input.ResourceTypes.ToList()
                : GetDefaultResourceTypes();

            // Determine file extension based on output format (used for all worker outputs)
            var fileExtension = input.OutputFormat == ExportConstants.MediaTypeParquet
                ? ".parquet"
                : ".ndjson";

            // Phase 1: For EACH resource type, get its surrogate ID ranges
            var allWorkerTasks = new List<Task<ExportWorkerOutput>>();

            try
            {
                foreach (var resourceType in resourceTypes)
                {
                    // Step 1: Determine partitions (surrogate ID ranges) for this resource type
                    var getRangesInput = new GetExportRangesInput(
                        TenantId: input.TenantId,
                        ResourceType: resourceType,
                        NumberOfRanges: rangesPerType);

                    var rangesOutput = await context.ScheduleTask<GetExportRangesOutput>(
                        typeof(GetExportRangesActivity),
                        getRangesInput);

                    // Step 2: For EACH range, queue a worker activity (all in parallel)
                    foreach (var (startId, endId) in rangesOutput.Ranges)
                    {
                        var outputPath = $"partition/{input.TenantId}/export/{input.JobId}/{resourceType}-{startId}-{endId}{fileExtension}";

                        var workerInput = new ExportWorkerInput(
                            JobId: input.JobId,
                            TenantId: input.TenantId,
                            ResourceType: resourceType,
                            StartSurrogateId: startId,
                            EndSurrogateId: endId,
                            OutputPath: outputPath,
                            Since: input.Since,
                            TypeFilters: input.TypeFilters,
                            ViewDefinitionId: input.ViewDefinitionId,
                            GroupId: input.GroupId);

                        // Schedule worker task (doesn't wait - queues for parallel execution)
                        var workerTask = context.ScheduleTask<ExportWorkerOutput>(
                            typeof(ExportWorkerActivity),
                            workerInput);

                        allWorkerTasks.Add(workerTask);
                    }
                }
            }
            catch (Exception ex)
            {
                // Failure during initialization (GetExportRangesActivity or scheduling)
                try
                {
                    var initFailureCompleteInput = new CompleteJobInput(
                        JobId: input.JobId,
                        TenantId: input.TenantId,
                        Success: false,
                        ExportedFiles: new Dictionary<string, string>(),
                        TotalResourcesExported: 0,
                        ErrorMessage: $"Export initialization failed: {ex.Message}");

                    await context.ScheduleTask<bool>(
                        typeof(CompleteJobActivity),
                        initFailureCompleteInput);
                }
                catch
                {
                    // If even completing the job fails, just continue
                }

                return new ExportCoordinatorOutput(
                    Success: false,
                    TotalResourcesExported: 0,
                    TotalBytesWritten: 0,
                    WorkerResults: null,
                    ErrorMessage: $"Export initialization failed: {ex.Message}",
                    FailurePhase: "Initialization");
            }

            // Phase 2: Wait for ALL worker activities to complete in parallel
            // This is where we achieve high throughput - 24-48 workers running simultaneously
            ExportWorkerOutput[] completedWorkers;
            try
            {
                completedWorkers = await Task.WhenAll(allWorkerTasks);
            }
            catch (Exception ex)
            {
                // Failure during worker execution (one or more workers failed)
                try
                {
                    var workerFailureCompleteInput = new CompleteJobInput(
                        JobId: input.JobId,
                        TenantId: input.TenantId,
                        Success: false,
                        ExportedFiles: new Dictionary<string, string>(),
                        TotalResourcesExported: (int)totalResourcesExported,
                        ErrorMessage: $"Worker execution failed: {ex.Message}");

                    await context.ScheduleTask<bool>(
                        typeof(CompleteJobActivity),
                        workerFailureCompleteInput);
                }
                catch
                {
                    // If even completing the job fails, just continue
                }

                return new ExportCoordinatorOutput(
                    Success: false,
                    TotalResourcesExported: totalResourcesExported,
                    TotalBytesWritten: totalBytesWritten,
                    WorkerResults: workerResults.AsReadOnly(),
                    ErrorMessage: $"Worker execution failed: {ex.Message}",
                    FailurePhase: "WorkerExecution");
            }

            // Phase 3: Aggregate results from all workers
            foreach (var workerOutput in completedWorkers)
            {
                workerResults.Add(workerOutput);
                totalResourcesExported += workerOutput.ResourcesExported;
                totalBytesWritten += workerOutput.BytesWritten;
            }

            // Phase 4: Build exported files dictionary from worker results
            var exportedFiles = new Dictionary<string, string>();
            foreach (var workerOutput in workerResults)
            {
                var fileKey = $"{workerOutput.ResourceType}-{workerOutput.StartSurrogateId}-{workerOutput.EndSurrogateId}";
                var filePath = $"tenant/{input.TenantId}/export/{input.JobId}/{workerOutput.ResourceType}-{workerOutput.StartSurrogateId}-{workerOutput.EndSurrogateId}{fileExtension}";
                exportedFiles[fileKey] = filePath;
            }

            // Phase 5: Complete the job (update database with final results)
            var completeInput = new CompleteJobInput(
                JobId: input.JobId,
                TenantId: input.TenantId,
                Success: true,
                ExportedFiles: exportedFiles,
                TotalResourcesExported: (int)totalResourcesExported,
                ErrorMessage: null);

            await context.ScheduleTask<bool>(
                typeof(CompleteJobActivity),
                completeInput);

            // Return success result with detailed worker outputs
            return new ExportCoordinatorOutput(
                Success: true,
                TotalResourcesExported: totalResourcesExported,
                TotalBytesWritten: totalBytesWritten,
                WorkerResults: workerResults.AsReadOnly(),
                ErrorMessage: null,
                FailurePhase: null);
        }
        catch (Exception ex)
        {
            // Unexpected failure during aggregation or final processing
            // This should be rare - most failures are caught in the specific phases above
            // Still update the job status to Failed
            try
            {
                var failureCompleteInput = new CompleteJobInput(
                    JobId: input.JobId,
                    TenantId: input.TenantId,
                    Success: false,
                    ExportedFiles: new Dictionary<string, string>(),
                    TotalResourcesExported: (int)totalResourcesExported,
                    ErrorMessage: $"Unexpected failure during export: {ex.Message}");

                await context.ScheduleTask<bool>(
                    typeof(CompleteJobActivity),
                    failureCompleteInput);
            }
            catch
            {
                // If even completing the job fails, just log it and return error
                // The orchestration already failed anyway
            }

            return new ExportCoordinatorOutput(
                Success: false,
                TotalResourcesExported: totalResourcesExported,
                TotalBytesWritten: totalBytesWritten,
                WorkerResults: workerResults.AsReadOnly(),
                ErrorMessage: $"Unexpected failure during export: {ex.Message}",
                FailurePhase: "Aggregation");
        }
    }

    private static List<string> GetDefaultResourceTypes()
    {
        return new List<string>
        {
            "Patient",
            "Observation",
            "Condition",
            "MedicationRequest",
            "Encounter",
            "Procedure",
        };
    }
}
