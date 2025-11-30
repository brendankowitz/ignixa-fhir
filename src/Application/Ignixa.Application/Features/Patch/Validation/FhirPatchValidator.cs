using System;

namespace Ignixa.Application.Features.Patch.Validation;

/// <summary>
/// Validates FHIR Patch Parameters resource structure.
/// </summary>
public class FhirPatchValidator
{
    /// <summary>
    /// Validate an array of patch operations.
    /// </summary>
    /// <param name="operations">Operations to validate</param>
    /// <exception cref="FhirPatchException">Thrown if validation fails</exception>
    public void Validate(FhirPatchOperation[] operations)
    {
        if (operations == null || operations.Length == 0)
        {
            throw new FhirPatchException("At least one patch operation is required");
        }

        for (int i = 0; i < operations.Length; i++)
        {
            ValidateOperation(operations[i], i);
        }
    }

    private void ValidateOperation(FhirPatchOperation operation, int index)
    {
        // Validate operation type
        if (!Enum.IsDefined(typeof(FhirPatchOperationType), operation.Type))
        {
            throw new FhirPatchException($"Operation {index}: Invalid operation type '{operation.Type}'");
        }

        // Validate path/source/destination requirements
        switch (operation.Type)
        {
            case FhirPatchOperationType.Add:
                ValidateRequired(operation.Path, "path", index);
                ValidateRequired(operation.Value, "value", index);
                break;

            case FhirPatchOperationType.Insert:
                ValidateRequired(operation.Path, "path", index);
                ValidateRequired(operation.Value, "value", index);
                ValidateRequired(operation.Index, "index", index);
                break;

            case FhirPatchOperationType.Delete:
                ValidateRequired(operation.Path, "path", index);
                break;

            case FhirPatchOperationType.Replace:
                ValidateRequired(operation.Path, "path", index);
                ValidateRequired(operation.Value, "value", index);
                break;

            case FhirPatchOperationType.Move:
                ValidateRequired(operation.Source, "source", index);
                ValidateRequired(operation.Destination, "destination", index);
                break;
        }
    }

    private void ValidateRequired(object? value, string parameterName, int operationIndex)
    {
        if (value == null || (value is string str && string.IsNullOrWhiteSpace(str)))
        {
            throw new FhirPatchException($"Operation {operationIndex}: Parameter '{parameterName}' is required");
        }
    }
}
