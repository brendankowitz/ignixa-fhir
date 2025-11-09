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
    [Description("Patch a FHIR resource using FHIRPath Patch operations. Advanced tool for complex multi-field patches. " +
        "For simple field updates, use patch_resource_field instead. " +
        "Example: patch_fhir_resource(resourceType='Patient', resourceId='patient-123', " +
        "operationsJson='[{\"type\":\"replace\",\"path\":\"Patient.active\",\"value\":true}]')")]
    public async Task<PatchResourceResultDto> PatchResourceAsync(
        [Description("Resource type: 'Patient', 'Observation', 'Condition', etc.")]
        string resourceType,

        [Description("Resource ID to patch")]
        string resourceId,

        [Description("JSON array of patch operations. Each operation: {\"type\":\"replace\"|\"add\"|\"delete\"|\"insert\"|\"move\",\"path\":\"FHIRPath\",\"value\":any,\"index\":number}. " +
            "Example: '[{\"type\":\"replace\",\"path\":\"Patient.active\",\"value\":true},{\"type\":\"replace\",\"path\":\"Patient.name[0].given[0]\",\"value\":\"John\"}]'")]
        string operationsJson,

        [Description("Optional ETag for version control (e.g., '5')")]
        string? ifMatch = null,

        [Description("Optional Tenant ID")]
        int? tenantId = null,

        CancellationToken cancellationToken = default)
    {
        // Validate input parameters and return errors in result (not exceptions)
        var validationError = ValidateInput(resourceType, resourceId, operationsJson);
        if (validationError != null)
        {
            return new PatchResourceResultDto
            {
                Success = false,
                ErrorMessage = validationError,
                PatchedResource = null
            };
        }

        // Parse operationsJson into PatchOperationDto list
        IReadOnlyList<PatchOperationDto> operations;
        try
        {
            operations = ParseOperationsJson(operationsJson);
        }
        catch (Exception ex)
        {
            return new PatchResourceResultDto
            {
                Success = false,
                ErrorMessage = $"Invalid operationsJson: {ex.Message}",
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
            var patchDocument = BuildPatchParameters(operations);

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
    private static string? ValidateInput(string? resourceType, string? resourceId, string? operationsJson)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            return "resourceType is required (e.g., 'Patient', 'Observation')";
        }

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return "resourceId is required (the logical ID of the resource to patch)";
        }

        if (string.IsNullOrWhiteSpace(operationsJson))
        {
            return "operationsJson is required. Example: '[{\"type\": \"replace\", \"path\": \"Patient.active\", \"value\": true}]'";
        }

        return null;
    }

    /// <summary>
    /// Parse operationsJson string into PatchOperationDto list.
    /// </summary>
    private static IReadOnlyList<PatchOperationDto> ParseOperationsJson(string operationsJson)
    {
        using var jsonDoc = System.Text.Json.JsonDocument.Parse(operationsJson);
        var operations = new List<PatchOperationDto>();

        if (jsonDoc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            throw new InvalidOperationException("operationsJson must be a JSON array");
        }

        foreach (var element in jsonDoc.RootElement.EnumerateArray())
        {
            if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw new InvalidOperationException("Each operation must be a JSON object");
            }

            string? type = element.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
            string? path = element.TryGetProperty("path", out var pathProp) ? pathProp.GetString() : null;
            int? index = element.TryGetProperty("index", out var indexProp) ? indexProp.GetInt32() : null;

            object? value = null;
            if (element.TryGetProperty("value", out var valueProp) && valueProp.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                value = valueProp.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.Number => valueProp.GetDecimal(),
                    System.Text.Json.JsonValueKind.String => valueProp.GetString(),
                    System.Text.Json.JsonValueKind.Array or System.Text.Json.JsonValueKind.Object => valueProp.GetRawText(),
                    _ => valueProp.GetString()
                };
            }

            if (string.IsNullOrWhiteSpace(type))
                throw new InvalidOperationException("Each operation must have a 'type' field");
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Each operation must have a 'path' field");

            operations.Add(new PatchOperationDto
            {
                Type = type,
                Path = path,
                Value = value,
                Index = index
            });
        }

        if (operations.Count == 0)
            throw new InvalidOperationException("operationsJson must contain at least one operation");

        return operations;
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
