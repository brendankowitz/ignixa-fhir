using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.Application.Features.Patch.Executors;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch;

/// <summary>
/// Applies FHIR Patch operations to a FHIR resource using strategy pattern.
/// Uses in-place mutation of the internal JsonObject for efficiency.
/// Refactored in Phase 2 to delegate to operation-specific executors.
/// </summary>
public class FhirPatchEngine
{
    private readonly ILogger<FhirPatchEngine> _logger;
    private readonly Dictionary<FhirPatchOperationType, IOperationExecutor> _executors;

    public FhirPatchEngine(
        ILogger<FhirPatchEngine> logger,
        IEnumerable<IOperationExecutor> executors)
    {
        _logger = logger;
        _executors = executors.ToDictionary(e => e.OperationType);
    }

    /// <summary>
    /// Apply patch operations to a resource using in-place mutation.
    /// Delegates to operation-specific executors via strategy pattern.
    /// </summary>
    public async Task<ResourceJsonNode> ApplyPatchAsync(
        ResourceJsonNode resource,
        FhirPatchOperation[] operations,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Applying {OperationCount} patch operations to {ResourceType}/{ResourceId}",
            operations.Length, resource.ResourceType, resource.Id);

        // Apply each operation in-place using the strategy pattern
        foreach (var operation in operations)
        {
            if (!_executors.TryGetValue(operation.Type, out var executor))
            {
                throw new FhirPatchException($"Unknown operation type: {operation.Type}");
            }

            // Executor mutates resource in-place and returns the same instance
            resource = await executor.ExecuteAsync(resource, operation, cancellationToken);
        }

        _logger.LogDebug("Successfully applied {OperationCount} patch operations",
            operations.Length);

        // Return the same resource instance (mutations were applied in-place)
        return resource;
    }
}
