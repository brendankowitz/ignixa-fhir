// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Text.Json.Nodes;
using Medino;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using Ignixa.Application.Features.Mcp.Tools;
using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Resource;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Mcp.Tools.FhirOperations;

/// <summary>
/// MCP tool for patching FHIR resources using FHIRPath Patch operations (Parameters resource).
/// Allows updating specific fields in resources via FHIRPath expressions and operation sequences.
/// </summary>
[McpServerToolType]
public class PatchResourceTool : TenantAwareMcpTool
{
    private readonly IMediator _mediator;

    public PatchResourceTool(
        IHttpContextAccessor httpContextAccessor,
        ITenantConfigurationStore tenantStore,
        IMediator mediator)
        : base(httpContextAccessor, tenantStore)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [McpServerTool(Name = "patch_fhir_resource")]
    [Description("Patch a FHIR resource using FHIRPath Patch operations. " +
        "Required: resourceType (e.g., 'Patient'), resourceId (e.g., 'patient-123'), and operations (array of patch operations). " +
        "Each operation must have: type ('replace'|'add'|'delete'|'insert'|'move'), path (FHIRPath like 'Patient.active' or 'Patient.name[0].given[0]'), " +
        "and value (required for add/replace operations). " +
        "Example call: resourceType='Patient', resourceId='patient-123', operations=[{type:'replace', path:'Patient.active', value:true}, {type:'replace', path:'Patient.name[0].given[0]', value:'John'}]. " +
        "Returns the patched resource if successful, or error details if the patch fails.")]
    public async Task<PatchResourceResultDto> PatchResourceAsync(
        [Description("(Required) Resource type to patch: 'Patient', 'Observation', 'Condition', etc. Example: 'Patient'")]
        string resourceType,

        [Description("(Required) Logical ID of the resource to patch. Example: 'patient-123' or 'obs-456'")]
        string resourceId,

        [Description("(Required) Array of patch operations to apply. Each operation is: {type: 'replace'|'add'|'delete'|'insert'|'move', path: 'FHIRPath expression', value: any, index?: number}. " +
            "Example: [{type: 'replace', path: 'Patient.active', value: true}, {type: 'replace', path: 'Patient.name[0].given[0]', value: 'John'}]")]
        IReadOnlyList<PatchOperationDto> operations,

        [Description("(Optional) ETag for optimistic concurrency control. Set to the version number (e.g., '5') to ensure only that version is patched")]
        string? ifMatch = null,

        [Description("(Optional) Tenant ID. Auto-detected if single-tenant, required if multi-tenant")]
        int? tenantId = null,

        CancellationToken cancellationToken = default)
    {
        // Validate input parameters and return errors in result (not exceptions)
        var validationError = ValidateInput(resourceType, resourceId, operations);
        if (validationError != null)
        {
            return new PatchResourceResultDto
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
            return new PatchResourceResultDto
            {
                Success = false,
                ErrorMessage = $"Tenant resolution failed: {ex.Message}",
                PatchedResource = null
            };
        }

        try
        {
            // Build Parameters resource with patch operations
            var patchDocument = BuildPatchParameters(operations!);

            // Execute patch via mediator
            var command = new PatchResourceCommand(
                TenantId: resolvedTenantId,
                ResourceType: resourceType,
                ResourceId: resourceId,
                PatchDocument: patchDocument,
                IfMatch: ifMatch);

            var patchedResource = await _mediator.SendAsync(command, cancellationToken);

            if (patchedResource == null)
            {
                return new PatchResourceResultDto
                {
                    Success = false,
                    ErrorMessage = $"Resource {resourceType}/{resourceId} not found",
                    PatchedResource = null
                };
            }

            return new PatchResourceResultDto
            {
                Success = true,
                ErrorMessage = null,
                PatchedResource = patchedResource.Resource
            };
        }
        catch (Exception ex)
        {
            return new PatchResourceResultDto
            {
                Success = false,
                ErrorMessage = $"Patch operation failed: {ex.Message}",
                PatchedResource = null
            };
        }
    }

    /// <summary>
    /// Validate input parameters and return error message if invalid, null if valid.
    /// Returns errors in the DTO result rather than throwing exceptions.
    /// </summary>
    private static string? ValidateInput(string? resourceType, string? resourceId, IReadOnlyList<PatchOperationDto> operations)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            return "resourceType is required (e.g., 'Patient', 'Observation')";
        }

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return "resourceId is required (the logical ID of the resource to patch)";
        }

        if (operations.Count == 0)
        {
            return "operations must contain at least one patch operation. " +
                   "Example: [{\"type\": \"replace\", \"path\": \"Patient.active\", \"value\": true}]";
        }

        // Validate each operation
        for (int i = 0; i < operations.Count; i++)
        {
            var op = operations[i];

            if (string.IsNullOrWhiteSpace(op.Type))
            {
                return $"operations[{i}].type is required (must be 'add', 'replace', 'delete', 'insert', or 'move')";
            }

            if (!IsValidOperationType(op.Type))
            {
                return $"operations[{i}].type '{op.Type}' is invalid. Must be one of: add, replace, delete, insert, move";
            }

            if (string.IsNullOrWhiteSpace(op.Path))
            {
                return $"operations[{i}].path is required (must be a FHIRPath expression like 'Patient.active' or 'Patient.name[0].given')";
            }

            // Value is required for add and replace
            if ((string.Equals(op.Type, "add", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(op.Type, "replace", StringComparison.OrdinalIgnoreCase)) &&
                op.Value == null)
            {
                return $"operations[{i}].value is required for '{op.Type}' operations";
            }

            // Index is required for insert and move
            if ((string.Equals(op.Type, "insert", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(op.Type, "move", StringComparison.OrdinalIgnoreCase)) &&
                !op.Index.HasValue)
            {
                return $"operations[{i}].index is required for '{op.Type}' operations";
            }
        }

        return null;
    }

    /// <summary>
    /// Check if the operation type is valid.
    /// </summary>
    private static bool IsValidOperationType(string type)
    {
        return type != null && (
            string.Equals(type, "add", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "replace", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "delete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "insert", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "move", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Build a Parameters resource from patch operation DTOs.
    /// Each operation becomes an "operation" parameter with parts for type, path, value, and index.
    /// </summary>
    private static ParametersJsonNode BuildPatchParameters(IReadOnlyList<PatchOperationDto> operations)
    {
        var parameters = new ParametersJsonNode();
        var parameterArray = new JsonArray();

        foreach (var op in operations)
        {
            if (string.IsNullOrWhiteSpace(op.Type))
            {
                throw new ArgumentException("Operation type (add/replace/delete/insert/move) is required");
            }

            if (string.IsNullOrWhiteSpace(op.Path))
            {
                throw new ArgumentException("Operation path (FHIRPath expression) is required");
            }

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
                var valuePart = new JsonObject { { "name", "value" } };

                // Handle different value types
                if (op.Value is bool boolValue)
                {
                    valuePart["valueBoolean"] = boolValue;
                }
                else if (op.Value is int intValue)
                {
                    valuePart["valueInteger"] = intValue;
                }
                else if (op.Value is double doubleValue)
                {
                    valuePart["valueDecimal"] = doubleValue;
                }
                else if (op.Value is string stringValue)
                {
                    valuePart["valueString"] = stringValue;
                }
                else if (op.Value is JsonNode jsonNode)
                {
                    // If it's already a JsonNode, serialize and add as JSON
                    valuePart["valueString"] = jsonNode.ToJsonString();
                }
                else
                {
                    // Fallback: serialize as JSON string
                    valuePart["valueString"] = System.Text.Json.JsonSerializer.Serialize(op.Value);
                }

                partsArray.Add(valuePart);
            }

            // Add index part if provided (for insert/move operations)
            if (op.Index.HasValue && (string.Equals(op.Type, "insert", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(op.Type, "move", StringComparison.OrdinalIgnoreCase)))
            {
                var indexPart = new JsonObject
                {
                    { "name", "index" },
                    { "valueInteger", op.Index.Value }
                };
                partsArray.Add(indexPart);
            }

            operationObj["part"] = partsArray;
            parameterArray.Add(operationObj);
        }

        parameters.MutableNode["parameter"] = parameterArray;
        return parameters;
    }
}

/// <summary>
/// DTO for a single FHIRPath Patch operation.
/// </summary>
public class PatchOperationDto
{
    /// <summary>
    /// Operation type: "add", "replace", "delete", "insert", or "move"
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// FHIRPath expression identifying the element(s) to modify
    /// Examples: "Patient.active", "Patient.name[0].given[0]", "Patient.name.where(use='official').family"
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Value to add or replace with. Required for "add" and "replace", optional for others.
    /// Can be a primitive (string, number, boolean) or a complex JSON object.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Array index for "insert" and "move" operations.
    /// </summary>
    public int? Index { get; init; }
}

/// <summary>
/// DTO for patch operation result.
/// </summary>
public class PatchResourceResultDto
{
    /// <summary>
    /// Whether the patch operation succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if the operation failed, null if successful.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The patched resource if successful, null if operation failed.
    /// </summary>
    public ResourceJsonNode? PatchedResource { get; init; }
}
