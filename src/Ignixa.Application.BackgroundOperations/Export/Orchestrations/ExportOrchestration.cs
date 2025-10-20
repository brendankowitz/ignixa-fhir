using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Export.Activities;

namespace Ignixa.Application.BackgroundOperations.Export.Orchestrations;

/// <summary>
/// Durable Task orchestration for FHIR bulk export operations.
/// Coordinates searching resources, writing NDJSON files, and updating job status.
/// </summary>
public class ExportOrchestration : TaskOrchestration<ExportOrchestrationOutput, ExportOrchestrationInput>
{
    public override async Task<ExportOrchestrationOutput> RunTask(
        OrchestrationContext context,
        ExportOrchestrationInput input)
    {
        var exportedFiles = new Dictionary<string, string>();
        var totalResourcesExported = 0;

        try
        {
            // Determine which resource types to export
            var resourceTypes = input.ResourceTypes.Any()
                ? input.ResourceTypes.ToList()
                : GetDefaultResourceTypes();

            // Process each resource type
            foreach (var resourceType in resourceTypes)
            {
                var outputPath = $"tenant/{input.TenantId}/export/{input.JobId}/{resourceType}.ndjson";
                string? continuationToken = null;
                int resourceTypeTotal = 0;

                // Get type filter for this resource type (if any)
                input.TypeFilters.TryGetValue(resourceType, out var typeFilter);

                // Process in chunks until no more continuation tokens
                do
                {
                    var chunkInput = new SearchAndWriteChunkInput(
                        TenantId: input.TenantId,
                        ResourceType: resourceType,
                        OutputPath: outputPath,
                        ContinuationToken: continuationToken,
                        TypeFilter: typeFilter);

                    var chunkOutput = await context.ScheduleTask<SearchAndWriteChunkOutput>(
                        typeof(SearchAndWriteChunkActivity),
                        chunkInput);

                    if (chunkOutput.ResourceCount == 0)
                    {
                        break; // No more resources for this type
                    }

                    resourceTypeTotal += chunkOutput.ResourceCount;
                    continuationToken = chunkOutput.ContinuationToken;

                    // Continue while we have a continuation token
                }
                while (continuationToken != null);

                // Only add to exported files if we actually exported resources
                if (resourceTypeTotal > 0)
                {
                    exportedFiles[resourceType] = outputPath;
                    totalResourcesExported += resourceTypeTotal;
                }
            }

            // Update job status to completed
            var completeInput = new CompleteJobInput(
                JobId: input.JobId,
                Success: true,
                ExportedFiles: exportedFiles,
                TotalResourcesExported: totalResourcesExported,
                ErrorMessage: null);

            await context.ScheduleTask<bool>(
                typeof(CompleteJobActivity),
                completeInput);

            return new ExportOrchestrationOutput(
                Success: true,
                ExportedFiles: exportedFiles,
                TotalResourcesExported: totalResourcesExported,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            // Mark job as failed
            var failInput = new CompleteJobInput(
                JobId: input.JobId,
                Success: false,
                ExportedFiles: exportedFiles,
                TotalResourcesExported: totalResourcesExported,
                ErrorMessage: ex.Message);

            await context.ScheduleTask<bool>(
                typeof(CompleteJobActivity),
                failInput);

            return new ExportOrchestrationOutput(
                Success: false,
                ExportedFiles: exportedFiles,
                TotalResourcesExported: totalResourcesExported,
                ErrorMessage: ex.Message);
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

/// <summary>
/// Input for the export orchestration.
/// </summary>
public record ExportOrchestrationInput(
    string JobId,
    int TenantId,
    IReadOnlyCollection<string> ResourceTypes,
    DateTimeOffset? Since,
    IReadOnlyDictionary<string, string> TypeFilters);

/// <summary>
/// Output from the export orchestration.
/// </summary>
public record ExportOrchestrationOutput(
    bool Success,
    Dictionary<string, string> ExportedFiles,
    int TotalResourcesExported,
    string? ErrorMessage);
