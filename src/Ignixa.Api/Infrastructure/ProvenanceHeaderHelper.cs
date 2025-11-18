// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Domain.Exceptions;
using Ignixa.Serialization.SourceNodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IO;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Helper for parsing and validating the X-Provenance header.
/// According to FHIR specification, the X-Provenance header allows clients to submit
/// provenance information along with POST/PUT operations.
/// The provenance SHALL NOT have a specified Provenance.target - the server will fill
/// the target reference after processing the main resource.
/// </summary>
public static class ProvenanceHeaderHelper
{
    private const string XProvenanceHeader = "X-Provenance";
    private const int MaxHeaderLength = 16384; // 16KB - typical IIS limit

    /// <summary>
    /// Attempts to parse the X-Provenance header from the request.
    /// </summary>
    /// <param name="headers">HTTP request headers.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <returns>Parsed Provenance resource as ResourceJsonNode, or null if header not present or invalid.</returns>
    /// <remarks>
    /// The provenance resource:
    /// - MUST be valid JSON
    /// - MUST have resourceType="Provenance"
    /// - MUST NOT have a 'target' property specified (server will auto-fill)
    /// - SHOULD have required Provenance elements (recorded, agent)
    /// Validation of the Provenance resource structure is delegated to the validation pipeline.
    /// </remarks>
    public static async Task<ResourceJsonNode?> TryParseProvenanceHeaderAsync(
        IHeaderDictionary headers,
        RecyclableMemoryStreamManager memoryStreamManager,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!headers.TryGetValue(XProvenanceHeader, out var headerValue))
        {
            return null;
        }

        var provenanceJson = headerValue.ToString();
        if (string.IsNullOrWhiteSpace(provenanceJson))
        {
            logger.LogWarning("X-Provenance header is empty");
            return null;
        }

        // Check header length to prevent abuse
        if (provenanceJson.Length > MaxHeaderLength)
        {
            logger.LogWarning(
                "X-Provenance header exceeds maximum length ({Length} > {MaxLength})",
                provenanceJson.Length,
                MaxHeaderLength);
            throw new BadRequestException(
                $"X-Provenance header is too long ({provenanceJson.Length} bytes). Maximum allowed: {MaxHeaderLength} bytes.");
        }

        logger.LogInformation("Processing X-Provenance header ({Length} bytes)", provenanceJson.Length);

        // Parse JSON to ResourceJsonNode
        ResourceJsonNode provenanceNode;
        try
        {
            await using (RecyclableMemoryStream memoryStream = memoryStreamManager.GetStream("x-provenance-header"))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(provenanceJson);
                await memoryStream.WriteAsync(bytes, cancellationToken);
                memoryStream.Position = 0;
                provenanceNode = await JsonSourceNodeFactory.Parse(memoryStream);
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "X-Provenance header contains invalid JSON");
            throw new BadRequestException("X-Provenance header contains invalid JSON", ex);
        }

        // Validate resource type is Provenance
        if (!string.Equals(provenanceNode.ResourceType, "Provenance", StringComparison.Ordinal))
        {
            logger.LogWarning(
                "X-Provenance header resourceType must be 'Provenance', got '{ResourceType}'",
                provenanceNode.ResourceType);
            throw new BadRequestException(
                $"X-Provenance header must contain a Provenance resource, got resourceType='{provenanceNode.ResourceType}'");
        }

        // Validate that target is NOT specified (per FHIR spec for X-Provenance)
        if (provenanceNode.MutableNode.AsObject().ContainsKey("target"))
        {
            logger.LogWarning("X-Provenance header should not contain 'target' property - server will auto-fill");
            throw new BadRequestException(
                "X-Provenance header must not specify 'target' property. The server will automatically set the target to the created/updated resource.");
        }

        logger.LogInformation("Successfully parsed X-Provenance header");
        return provenanceNode;
    }

    /// <summary>
    /// Creates a Provenance resource with the target reference set to the specified resource.
    /// </summary>
    /// <param name="provenanceTemplate">The original Provenance resource from X-Provenance header (without target).</param>
    /// <param name="targetResourceType">The resource type of the target (e.g., "Patient").</param>
    /// <param name="targetResourceId">The resource ID of the target.</param>
    /// <param name="targetVersionId">The version ID of the target resource.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <returns>A new ResourceJsonNode with the target reference populated.</returns>
    public static async Task<ResourceJsonNode> CreateProvenanceWithTargetAsync(
        ResourceJsonNode provenanceTemplate,
        string targetResourceType,
        string targetResourceId,
        string targetVersionId,
        RecyclableMemoryStreamManager memoryStreamManager,
        ILogger logger)
    {
        logger.LogInformation(
            "Creating Provenance resource with target {ResourceType}/{Id}/_history/{VersionId}",
            targetResourceType,
            targetResourceId,
            targetVersionId);

        // Clone the provenance node to avoid mutating the original
        var provenanceJson = provenanceTemplate.SerializeToString();
        var provenanceObject = JsonNode.Parse(provenanceJson)?.AsObject()
            ?? throw new InvalidOperationException("Failed to parse Provenance resource");

        // Add target reference array with version-specific reference
        var targetReference = $"{targetResourceType}/{targetResourceId}/_history/{targetVersionId}";
        var targetArray = new JsonArray
        {
            new JsonObject
            {
                ["reference"] = targetReference
            }
        };

        provenanceObject["target"] = targetArray;

        // Serialize back to ResourceJsonNode
        var updatedJson = provenanceObject.ToJsonString();
        await using (RecyclableMemoryStream memoryStream = memoryStreamManager.GetStream("provenance-with-target"))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(updatedJson);
            await memoryStream.WriteAsync(bytes);
            memoryStream.Position = 0;
            return await JsonSourceNodeFactory.Parse(memoryStream);
        }
    }
}
