using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.Application.Features.Patch.Executors;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;
using ISchema = Ignixa.Abstractions.ISchema;

namespace Ignixa.Application.Features.Patch;

/// <summary>
/// Applies FHIR Patch operations to a FHIR resource using strategy pattern.
/// Uses in-place mutation of the internal JsonObject for efficiency.
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
    /// Spec-form operations ('name' part, anonymous-type values) are resolved against <paramref name="schema"/>
    /// immediately before each one runs, so they see the result of the preceding operations.
    /// </summary>
    public async Task<ResourceJsonNode> ApplyPatchAsync(
        ResourceJsonNode resource,
        FhirPatchOperation[] operations,
        ISchema schema,
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

            var resolved = FhirPatchOperationResolver.Resolve(resource, operation, schema);

            // Executor mutates resource in-place and returns the same instance
            resource = await executor.ExecuteAsync(resource, resolved, cancellationToken);

            // Cached element views must not hide this operation's writes from the next one.
            resource.InvalidateCaches();
        }

        _logger.LogDebug("Successfully applied {OperationCount} patch operations",
            operations.Length);

        // Return the same resource instance (mutations were applied in-place)
        return resource;
    }
}
