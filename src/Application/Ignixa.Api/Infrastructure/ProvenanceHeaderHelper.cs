// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Domain.Exceptions;
using Ignixa.Serialization;
using Ignixa.Serialization.Extensions;
using Ignixa.Serialization.Models;
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
    /// <param name="memoryStreamManager">Memory stream manager for efficient parsing.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed Provenance resource as ProvenanceJsonNode, or null if header not present or invalid.</returns>
    /// <remarks>
    /// The provenance resource:
    /// - MUST be valid JSON
    /// - MUST have resourceType="Provenance"
    /// - MUST NOT have a 'target' property specified (server will auto-fill)
    /// - SHOULD have required Provenance elements (recorded, agent)
    /// Validation of the Provenance resource structure is delegated to the validation pipeline.
    /// </remarks>
    public static async Task<ProvenanceJsonNode?> TryParseProvenanceHeaderAsync(
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
        ResourceJsonNode resourceNode;
        try
        {
            await using (RecyclableMemoryStream memoryStream = memoryStreamManager.GetStream("x-provenance-header"))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(provenanceJson);
                await memoryStream.WriteAsync(bytes, cancellationToken);
                memoryStream.Position = 0;
                resourceNode = await JsonSourceNodeFactory.ParseAsync(memoryStream, cancellationToken);
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "X-Provenance header contains invalid JSON");
            throw new BadRequestException("X-Provenance header contains invalid JSON", ex);
        }

        // Validate resource type is Provenance
        if (!string.Equals(resourceNode.ResourceType, "Provenance", StringComparison.Ordinal))
        {
            logger.LogWarning(
                "X-Provenance header resourceType must be 'Provenance', got '{ResourceType}'",
                resourceNode.ResourceType);
            throw new BadRequestException(
                $"X-Provenance header must contain a Provenance resource, got resourceType='{resourceNode.ResourceType}'");
        }

        // Convert to strongly-typed ProvenanceJsonNode using extension method
        var provenanceNode = resourceNode.As<ProvenanceJsonNode>();

        // Validate that target is NOT specified (per FHIR spec for X-Provenance)
        if (provenanceNode.HasTarget)
        {
            logger.LogWarning("X-Provenance header should not contain 'target' property - server will auto-fill");
            throw new BadRequestException(
                "X-Provenance header must not specify 'target' property. The server will automatically set the target to the created/updated resource.");
        }

        logger.LogInformation("Successfully parsed X-Provenance header");
        return provenanceNode;
    }

    /// <summary>
    /// Attempts to parse the X-Provenance header for standalone operations only.
    /// When a coordinator is present (bundle operations), returns null since provenance
    /// should be handled via bundle entries, not headers.
    /// </summary>
    /// <param name="headers">HTTP request headers.</param>
    /// <param name="coordinator">Deferred write coordinator (null for standalone operations).</param>
    /// <param name="memoryStreamManager">Memory stream manager for efficient parsing.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed Provenance resource, or null if not applicable.</returns>
    public static async Task<ProvenanceJsonNode?> TryParseForStandaloneOperationAsync(
        IHeaderDictionary headers,
        object? coordinator,
        RecyclableMemoryStreamManager memoryStreamManager,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Only process provenance for standalone operations (not bundle operations)
        if (coordinator != null)
        {
            return null;
        }

        return await TryParseProvenanceHeaderAsync(headers, memoryStreamManager, logger, cancellationToken);
    }
}
