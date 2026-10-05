// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Domain.Exceptions;
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

        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (IsUrnUuid(entry.FullUrl) && !aliases.Add(entry.FullUrl!))
            {
                throw new BadRequestException($"Duplicate transaction fullUrl alias '{entry.FullUrl}'.");
            }
            if (IsUrnUuid(entry.FullUrl) && TransactionRequestValidator.IsExplicitPut(entry))
            {
                context.AddReference(entry.FullUrl!, $"{entry.ResourceType}/{entry.ResourceId}");
                continue;
            }
            if (entry.HttpVerb == "POST" && IsUrnUuid(entry.FullUrl))
            {
                // Assign a new GUID for this resource
                var assignedId = Guid.NewGuid().ToString();
                entry.AssignedResourceId = assignedId;

                // Map urn:uuid -> assigned ID
                context.AddReference(entry.FullUrl!, $"{entry.ResourceType}/{assignedId}");

                _logger.LogDebug(
                    "Assigned ID for POST entry {Index}: {UrnUuid} -> {AssignedId} (ResourceType={ResourceType})",
                    entry.Index,
                    entry.FullUrl,
                    assignedId,
                    entry.ResourceType);
            }
        }

        if (bundleType == BundleType.Transaction)
        {
            foreach (var entry in entries.Where(e => e.RawJson != null))
            {
                var json = JsonNode.Parse(entry.RawJson!);
                RewriteReferences(json, reference => IsUrnUuid(reference)
                    ? context.ResolveReference(reference) ?? throw new BadRequestException($"Unresolved transaction reference '{reference}'.")
                    : reference);
                entry.RawJson = json!.ToJsonString();
            }
        }

        _logger.LogInformation(
            "Pre-processing complete: {ReferenceCount} urn:uuid references mapped",
            context.Count);

        return context;
    }

    internal static bool RewriteReferenceAliases(JsonNode? node, IReadOnlyDictionary<string, string> aliases) =>
        RewriteReferences(node, reference => aliases.TryGetValue(reference, out var resolved) ? resolved : reference);

    private static bool RewriteReferences(JsonNode? node, Func<string, string> resolve)
    {
        var changed = false;
        if (node is JsonObject obj)
        {
            if (obj["reference"] is JsonValue value && value.TryGetValue<string>(out var reference))
            {
                var resolved = resolve(reference);
                if (resolved != reference)
                {
                    obj["reference"] = resolved;
                    changed = true;
                }
            }
            foreach (var property in obj)
            {
                changed |= RewriteReferences(property.Value, resolve);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                changed |= RewriteReferences(item, resolve);
            }
        }
        return changed;
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
