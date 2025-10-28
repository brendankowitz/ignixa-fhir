using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Patch;

/// <summary>
/// Parses a FHIR Parameters resource into FhirPatchOperation objects.
/// Used for FHIRPath Patch operations per FHIR R4 Section 3.1.0.7.1
/// </summary>
public class FhirPatchParametersParser
{
    /// <summary>
    /// Parse a Parameters resource (already parsed ResourceJsonNode) into an array of FhirPatchOperation.
    /// </summary>
    public FhirPatchOperation[] Parse(ResourceJsonNode parametersNode)
    {
        if (parametersNode == null)
        {
            throw new ArgumentNullException(nameof(parametersNode), "Parameters resource cannot be null");
        }

        // Cast to ParametersJsonNode
        if (parametersNode is not ParametersJsonNode parameters)
        {
            throw new FhirPatchException($"Expected ParametersJsonNode but got {parametersNode.GetType().Name}");
        }

        if (parameters.ResourceType != "Parameters")
        {
            throw new FhirPatchException(
                $"Expected resourceType 'Parameters', got '{parameters.ResourceType}'");
        }

        if (parameters.Parameter == null || parameters.Parameter.Count == 0)
        {
            throw new FhirPatchException("Parameters resource must contain at least one parameter");
        }

        // Extract all operation parameters
        var operations = new List<FhirPatchOperation>();

        foreach (var parameter in parameters.Parameter)
        {
            // Debug: Log parameter info
            var paramName = parameter.Name;
            if (paramName == "operation")
            {
                var operation = ParseOperation(parameter);
                operations.Add(operation);
            }
        }

        if (operations.Count == 0)
        {
            // Debug: Build diagnostic message with parameter names found
            var parameterNames = string.Join(", ", parameters.Parameter.Select(p => p.Name ?? "[null]"));
            throw new FhirPatchException($"Parameters resource must contain at least one 'operation' parameter. Found parameters: {parameterNames}");
        }

        return operations.ToArray();
    }

    private FhirPatchOperation ParseOperation(ParameterJsonNode operationParameter)
    {
        if (operationParameter.Part == null || operationParameter.Part.Count == 0)
        {
            throw new FhirPatchException("Operation parameter must contain 'part' array");
        }

        // Extract required 'type' part
        var typePart = operationParameter.FindPart("type");
        if (typePart == null)
        {
            throw new FhirPatchException("Operation parameter must contain 'type' part");
        }

        // Try to get valueCode or valueString
        var typeCode = typePart.GetValueAs<string>("valueCode") ?? typePart.GetValueAs<string>("valueString");
        if (string.IsNullOrEmpty(typeCode))
        {
            throw new FhirPatchException("Operation 'type' part must have a valueCode or valueString");
        }

        var operationType = ParseOperationType(typeCode);

        // Extract optional parts based on operation type
        var pathPart = operationParameter.FindPart("path");
        var valuePart = operationParameter.FindPart("value");
        var indexPart = operationParameter.FindPart("index");
        var sourcePart = operationParameter.FindPart("source");
        var destinationPart = operationParameter.FindPart("destination");

        // Validate required parts for each operation type
        ValidateOperationParts(operationType, pathPart, valuePart, indexPart, sourcePart, destinationPart);

        return new FhirPatchOperation
        {
            Type = operationType,
            Path = pathPart?.GetValueAs<string>("valueString"),
            Value = valuePart?.GetValue(),
            Index = indexPart?.GetValueAs<int?>("valueInteger"),
            Source = sourcePart?.GetValueAs<string>("valueString"),
            Destination = destinationPart?.GetValueAs<string>("valueString"),
        };
    }

    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "FHIR codes are conventionally lowercase")]
    private FhirPatchOperationType ParseOperationType(string typeCode)
    {
        return typeCode.ToLowerInvariant() switch
        {
            "add" => FhirPatchOperationType.Add,
            "insert" => FhirPatchOperationType.Insert,
            "delete" => FhirPatchOperationType.Delete,
            "replace" => FhirPatchOperationType.Replace,
            "move" => FhirPatchOperationType.Move,
            _ => throw new FhirPatchException(
                $"Unknown operation type '{typeCode}'. Must be one of: add, insert, delete, replace, move"),
        };
    }

    private void ValidateOperationParts(
        FhirPatchOperationType type,
        ParameterJsonNode pathPart,
        ParameterJsonNode valuePart,
        ParameterJsonNode indexPart,
        ParameterJsonNode sourcePart,
        ParameterJsonNode destinationPart)
    {
        switch (type)
        {
            case FhirPatchOperationType.Add:
                if (pathPart == null)
                    throw new FhirPatchException("Add operation requires 'path' part");
                if (valuePart == null)
                    throw new FhirPatchException("Add operation requires 'value' part");
                break;

            case FhirPatchOperationType.Insert:
                if (pathPart == null)
                    throw new FhirPatchException("Insert operation requires 'path' part");
                if (valuePart == null)
                    throw new FhirPatchException("Insert operation requires 'value' part");
                if (indexPart == null)
                    throw new FhirPatchException("Insert operation requires 'index' part");
                break;

            case FhirPatchOperationType.Delete:
                if (pathPart == null)
                    throw new FhirPatchException("Delete operation requires 'path' part");
                break;

            case FhirPatchOperationType.Replace:
                if (pathPart == null)
                    throw new FhirPatchException("Replace operation requires 'path' part");
                if (valuePart == null)
                    throw new FhirPatchException("Replace operation requires 'value' part");
                break;

            case FhirPatchOperationType.Move:
                if (sourcePart == null)
                    throw new FhirPatchException("Move operation requires 'source' part");
                if (destinationPart == null)
                    throw new FhirPatchException("Move operation requires 'destination' part");
                break;

            default:
                throw new FhirPatchException($"Unknown operation type: {type}");
        }
    }
}

/// <summary>
/// Exception thrown when FHIR Patch validation fails.
/// </summary>
public class FhirPatchException : Exception
{
    public FhirPatchException(string message) : base(message)
    {
    }

    public FhirPatchException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
