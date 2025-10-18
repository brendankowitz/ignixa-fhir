using System.Threading;
using System.Threading.Tasks;
using Ignixa.SourceNodeSerialization.ElementModel;

namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes a specific patch operation type on a FHIR resource.
/// </summary>
public interface IOperationExecutor
{
    /// <summary>
    /// Operation type this executor handles
    /// </summary>
    FhirPatchOperationType OperationType { get; }

    /// <summary>
    /// Execute the operation on the resource
    /// </summary>
    /// <param name="resource">Resource to patch (ITypedElement)</param>
    /// <param name="operation">Operation to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Patched resource (ITypedElement)</returns>
    Task<ITypedElement> ExecuteAsync(
        ITypedElement resource,
        FhirPatchOperation operation,
        CancellationToken cancellationToken);
}
