using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Patch.Executors;

/// <summary>
/// Executes FHIR Patch 'move' operations.
/// Moves a value from source path to destination path (delete + add).
/// Uses FHIRPath evaluation for navigation.
/// </summary>
public class MoveOperationExecutor : IOperationExecutor
{
    private readonly ILogger<MoveOperationExecutor> _logger;
    private readonly DeleteOperationExecutor _deleteExecutor;
    private readonly AddOperationExecutor _addExecutor;
    private readonly FhirPathPatchHelper _fhirPathHelper;

    public FhirPatchOperationType OperationType => FhirPatchOperationType.Move;

    public MoveOperationExecutor(
        ILogger<MoveOperationExecutor> logger,
        DeleteOperationExecutor deleteExecutor,
        AddOperationExecutor addExecutor,
        FhirPathPatchHelper fhirPathHelper)
    {
        _logger = logger;
        _deleteExecutor = deleteExecutor;
        _addExecutor = addExecutor;
        _fhirPathHelper = fhirPathHelper;
    }

    public async Task<ResourceJsonNode> ExecuteAsync(
        ResourceJsonNode resource,
        FhirPatchOperation operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(operation.Source))
        {
            throw new FhirPatchException("Move operation requires 'source'");
        }

        if (string.IsNullOrEmpty(operation.Destination))
        {
            throw new FhirPatchException("Move operation requires 'destination'");
        }

        // Get source value using FHIRPath
        var sourceMatches = _fhirPathHelper.EvaluateToJsonNodes(resource, operation.Source).ToList();
        if (sourceMatches.Count == 0)
        {
            throw new FhirPatchException($"Source path '{operation.Source}' not found");
        }

        if (sourceMatches.Count > 1)
        {
            throw new FhirPatchException($"Source path '{operation.Source}' matched multiple elements (expected 1)");
        }

        var sourceValue = sourceMatches[0];

        // Remove from source
        var deleteOp = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Delete,
            Path = operation.Source,
        };
        resource = await _deleteExecutor.ExecuteAsync(resource, deleteOp, cancellationToken);

        // Add to destination
        var addOp = new FhirPatchOperation
        {
            Type = FhirPatchOperationType.Add,
            Path = operation.Destination,
            Value = sourceValue,
        };
        resource = await _addExecutor.ExecuteAsync(resource, addOp, cancellationToken);

        _logger.LogDebug("Moved value from {Source} to {Destination}", operation.Source, operation.Destination);

        return resource;
    }
}
