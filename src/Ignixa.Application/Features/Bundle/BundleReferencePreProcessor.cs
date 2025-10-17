// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Pre-processes bundle entries to assign resource IDs for POST operations
/// and build the reference resolution map for urn:uuid references.
/// This phase runs before bundle execution to enable reference resolution.
/// </summary>
public class BundleReferencePreProcessor
{
    private readonly ILogger<BundleReferencePreProcessor> _logger;

    public BundleReferencePreProcessor(ILogger<BundleReferencePreProcessor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Pre-processes bundle entries to:
    /// 1. Assign resource IDs to POST operations (creates)
    /// 2. Build reference map from urn:uuid to assigned IDs
    /// 3. Return context for reference resolution during execution
    /// </summary>
    /// <param name="entries">The parsed bundle entries.</param>
    /// <param name="bundleType">The bundle type (Transaction or Batch).</param>
    /// <returns>Reference resolution context with mappings.</returns>
    public ReferenceResolutionContext PreProcessReferences(
        IReadOnlyList<BundleEntryContext> entries,
        BundleType bundleType)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var context = new ReferenceResolutionContext();

        _logger.LogDebug(
            "Pre-processing {Count} bundle entries for reference resolution (BundleType={BundleType})",
            entries.Count,
            bundleType);

        // Scan all POST operations that have urn:uuid fullUrl
        foreach (var entry in entries)
        {
            if (entry.HttpVerb == "POST" && IsUrnUuid(entry.FullUrl))
            {
                // Assign a new GUID for this resource
                var assignedId = Guid.NewGuid().ToString();
                entry.AssignedResourceId = assignedId;

                // Map urn:uuid -> assigned ID
                context.AddReference(entry.FullUrl!, assignedId);

                _logger.LogDebug(
                    "Assigned ID for POST entry {Index}: {UrnUuid} -> {AssignedId} (ResourceType={ResourceType})",
                    entry.Index,
                    entry.FullUrl,
                    assignedId,
                    entry.ResourceType);
            }
        }

        _logger.LogInformation(
            "Pre-processing complete: {ReferenceCount} urn:uuid references mapped",
            context.Count);

        return context;
    }

    /// <summary>
    /// Checks if a string is a urn:uuid reference.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <returns>True if the string is a urn:uuid reference; otherwise, false.</returns>
    private static bool IsUrnUuid(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase);
    }
}
