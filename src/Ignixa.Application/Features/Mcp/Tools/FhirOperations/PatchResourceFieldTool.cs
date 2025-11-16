// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Text.Json.Nodes;
using Medino;
using ModelContextProtocol.Server;
using Ignixa.Application.Features.Mcp.Tools;
using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Mcp.Tools.FhirOperations;

/// <summary>
/// MCP tool for patching a single field in a FHIR resource.
/// Use this for simple field updates. For complex multi-operation patches, use patch_fhir_resource.
/// </summary>
[McpServerToolType]
public class PatchResourceFieldTool : TenantAwareMcpTool
{
    private readonly IMediator _mediator;

    public PatchResourceFieldTool(
        IFhirRequestContextAccessor fhirRequestContextAccessor,
        ITenantConfigurationStore tenantStore,
        IMediator mediator)
        : base(fhirRequestContextAccessor, tenantStore)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [McpServerTool(Name = "patch_resource_field")]
    [Description("Patch a single field in a FHIR resource. Simple interface for updating one field at a time. " +
        "Examples: Set active=true, change name, update birthDate, etc. " +
        "For complex multi-field patches, use patch_fhir_resource instead.")]
    public async Task<PatchResourceFieldResultDto> PatchResourceFieldAsync(
        [Description("Resource type: Patient, Observation, Condition, etc.")]
        string resourceType,

        [Description("Resource ID to patch")]
        string resourceId,

        [Description("FHIRPath to the field: 'active', 'name[0].given', 'birthDate', 'status', etc.")]
        string fieldPath,

        [Description("New value: string, number, boolean, or JSON array/object")]
        object value,

        [Description("Operation: 'set' (replace/add), 'delete', 'add', 'remove'")]
        string operation = "set",

        [Description("Tenant ID (optional - auto-detected if single tenant)")]
        int? tenantId = null,

        CancellationToken cancellationToken = default)
    {
        // Validate inputs
        var validationError = ValidateInput(resourceType, resourceId, fieldPath, operation);
        if (validationError != null)
        {
            return new PatchResourceFieldResultDto
            {
                Success = false,
                ErrorMessage = validationError,
                PatchedResource = null
            };
        }

        // Resolve tenant using base class logic
        int resolvedTenantId;
        try
        {
            resolvedTenantId = await ResolveTenantIdAsync(tenantId, cancellationToken);
        }
        catch (Exception ex)
        {
            return new PatchResourceFieldResultDto
            {
                Success = false,
                ErrorMessage = $"Tenant resolution failed: {ex.Message}",
                PatchedResource = null
            };
        }

        try
        {
            // Convert simple operation to FHIRPath patch operation type
            var operationType = ConvertOperationType(operation);

            // Build single patch operation
            var patchOperations = new List<PatchOperationDto>
            {
                new PatchOperationDto
                {
                    Type = operationType,
                    Path = fieldPath,
                    Value = operationType == "delete" ? null : value
                }
            };

            // Build Parameters resource with patch operations
            var patchDocument = BuildPatchParameters(patchOperations);

            // Execute patch via mediator
            var command = new PatchResourceCommand(
                TenantId: resolvedTenantId,
                ResourceType: resourceType,
                ResourceId: resourceId,
                PatchDocument: patchDocument,
                IfMatch: null);

            var patchedResource = await _mediator.SendAsync(command, cancellationToken);

            if (patchedResource == null)
            {
                return new PatchResourceFieldResultDto
                {
                    Success = false,
                    ErrorMessage = $"Resource {resourceType}/{resourceId} not found",
                    PatchedResource = null
                };
            }

            return new PatchResourceFieldResultDto
            {
                Success = true,
                ErrorMessage = null,
                PatchedResource = patchedResource.Resource
            };
        }
        catch (Exception ex)
        {
            return new PatchResourceFieldResultDto
            {
                Success = false,
                ErrorMessage = $"Patch operation failed: {ex.Message}",
                PatchedResource = null
            };
        }
    }

    /// <summary>
    /// Validate input parameters and return error message if invalid, null if valid.
    /// </summary>
    private static string? ValidateInput(string? resourceType, string? resourceId, string? fieldPath, string? operation)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            return "resourceType is required (e.g., 'Patient', 'Observation')";
        }

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return "resourceId is required";
        }

        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return "fieldPath is required (e.g., 'active', 'name[0].given', 'birthDate')";
        }

        if (!IsValidOperation(operation))
        {
            return "operation must be one of: 'set', 'delete', 'add', 'remove'";
        }

        return null;
    }

    /// <summary>
    /// Check if operation is valid.
    /// </summary>
    private static bool IsValidOperation(string? operation)
    {
        return operation != null && (
            string.Equals(operation, "set", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(operation, "delete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(operation, "add", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(operation, "remove", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Convert simple operation names to FHIRPath patch operation types.
    /// </summary>
    private static string ConvertOperationType(string operation)
    {
        if (string.Equals(operation, "set", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(operation, "replace", StringComparison.OrdinalIgnoreCase))
        {
            return "replace";
        }

        if (string.Equals(operation, "delete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(operation, "remove", StringComparison.OrdinalIgnoreCase))
        {
            return "delete";
        }

        if (string.Equals(operation, "add", StringComparison.OrdinalIgnoreCase))
        {
            return "add";
        }

        return "replace";
    }

    /// <summary>
    /// Build a Parameters resource from a single patch operation.
    /// </summary>
    private static ParametersJsonNode BuildPatchParameters(List<PatchOperationDto> operations)
    {
        var parameters = new ParametersJsonNode();
        var parameterArray = new JsonArray();

        foreach (var op in operations)
        {
            // Create operation parameter object
            var operationObj = new JsonObject { { "name", "operation" } };
            var partsArray = new JsonArray();

            // Add type part
            var typePart = new JsonObject
            {
                { "name", "type" },
                { "valueCode", op.Type }
            };
            partsArray.Add(typePart);

            // Add path part
            var pathPart = new JsonObject
            {
                { "name", "path" },
                { "valueString", op.Path }
            };
            partsArray.Add(pathPart);

            // Add value part if provided and not a delete operation
            if (op.Value != null && !string.Equals(op.Type, "delete", StringComparison.OrdinalIgnoreCase))
            {
                var valuePart = CreateValuePart(op.Value);
                partsArray.Add(valuePart);
            }

            operationObj["part"] = partsArray;
            parameterArray.Add(operationObj);
        }

        parameters.MutableNode["parameter"] = parameterArray;
        return parameters;
    }

    /// <summary>
    /// Creates a FHIR Parameters part for a value, handling type conversion.
    /// </summary>
    private static JsonObject CreateValuePart(object? value)
    {
        var valuePart = new JsonObject { { "name", "value" } };

        // Handle different value types
        if (value is bool boolValue)
        {
            valuePart["valueBoolean"] = boolValue;
        }
        else if (value is int intValue)
        {
            valuePart["valueInteger"] = intValue;
        }
        else if (value is double doubleValue)
        {
            valuePart["valueDecimal"] = doubleValue;
        }
        else if (value is string stringValue)
        {
            valuePart["valueString"] = stringValue;
        }
        else if (value is JsonNode jsonNode)
        {
            // If it's already a JsonNode, serialize and add as JSON
            valuePart["valueString"] = jsonNode.ToJsonString();
        }
        else
        {
            // Fallback: serialize as JSON string
            valuePart["valueString"] = System.Text.Json.JsonSerializer.Serialize(value);
        }

        return valuePart;
    }
}
