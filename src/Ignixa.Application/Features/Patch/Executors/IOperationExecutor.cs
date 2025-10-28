using System.Threading;
using System.Threading.Tasks;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes a specific FHIR Patch operation type.
/// Part of strategy pattern refactor (Phase 2).
/// </summary>
public interface IOperationExecutor
{
    /// <summary>
    /// The FHIR Patch operation type this executor handles.
    /// </summary>
    FhirPatchOperationType OperationType { get; }

    /// <summary>
    /// Execute the operation on the resource, mutating it in-place.
    /// </summary>
    /// <param name="resource">The FHIR resource to mutate.</param>
    /// <param name="operation">The patch operation to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The mutated resource (same instance).</returns>
    Task<ResourceJsonNode> ExecuteAsync(
        ResourceJsonNode resource,
        FhirPatchOperation operation,
        CancellationToken cancellationToken);
}
