// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// Removes references to a deleted resource, matching microsoft/fhir-server's ReferenceRemover semantics.
/// For any JSON object whose "reference" string property resolves to the target, removes the "reference"
/// property and sets the "display" property to "Referenced resource deleted". Recurses through all objects
/// and arrays; other siblings like "identifier" and "type" are preserved.
/// </summary>
public static class BulkDeleteReferenceRemover
{
    public const string RemovedReferenceDisplay = "Referenced resource deleted";

    /// <summary>
    /// Removes all references to the target resource from a JSON resource tree.
    /// </summary>
    /// <param name="resource">The JSON resource node to process. Must not be null.</param>
    /// <param name="target">The reference value to match and remove, as <c>Type/id</c>. Must not be null or whitespace.</param>
    /// <param name="referenceParser">
    /// The parser the reference search index is built with, used to resolve a stored reference to the
    /// (type, id) pair the index holds for it. Required rather than optional: referrers are found through
    /// that index, so a caller without the parser silently leaves behind exactly the referrers whose
    /// reference is written in a form the index normalized -- see the remarks on
    /// <see cref="ResolvesToTarget"/>.
    /// </param>
    /// <returns>true if at least one reference was removed; false if no references matched.</returns>
    /// <exception cref="ArgumentNullException">Thrown when resource, target or referenceParser is null.</exception>
    /// <exception cref="ArgumentException">Thrown when target is whitespace only.</exception>
    public static bool RemoveReferences(
        JsonNode resource,
        string target,
        IReferenceSearchValueParser referenceParser)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(referenceParser);

        return RemoveReferencesInternal(resource, target, referenceParser);
    }

    private static bool RemoveReferencesInternal(JsonNode? node, string target, IReferenceSearchValueParser referenceParser)
    {
        var changed = false;

        if (node is JsonObject obj)
        {
            // First, collect all changes to make to this object, and recurse into children.
            // We cannot modify obj while iterating its properties.
            var hasMatchingReference = false;

            // Check if this object has a "reference" property that resolves to the target.
            if (obj["reference"] is JsonValue value && value.TryGetValue<string>(out var reference))
            {
                hasMatchingReference = ResolvesToTarget(reference, target, referenceParser);
            }

            // Recurse into all child properties.
            foreach (var property in obj)
            {
                changed |= RemoveReferencesInternal(property.Value, target, referenceParser);
            }

            // Now apply the change to this object if needed.
            if (hasMatchingReference)
            {
                obj.Remove("reference");
                obj["display"] = RemovedReferenceDisplay;
                changed = true;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                changed |= RemoveReferencesInternal(item, target, referenceParser);
            }
        }

        return changed;
    }

    /// <summary>
    /// Whether <paramref name="reference"/> points at <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A whole-string comparison is not enough. Referrers are found through the reference search index,
    /// and the index stores a normalized (type, id) pair: <c>ReferenceSearchValueParser</c> drops a
    /// <c>/_history/{version}</c> suffix and collapses an absolute URL under one of this server's service
    /// base URIs onto the relative form. A search for the target therefore returns referrers written as
    /// <c>Patient/p1/_history/2</c> or <c>https://this-server/Patient/p1</c>, and matching only the literal
    /// <c>Patient/p1</c> would leave exactly those referrers pointing at a resource about to be hard
    /// deleted -- silently, since an unchanged referrer is skipped rather than reported.
    /// </para>
    /// <para>
    /// So the parser does the structural work and the comparison below decides identity. The two stay
    /// separate deliberately: the parser's resource-type pattern is case-sensitive and would not recognize
    /// <c>PATIENT/P1</c> at all, while SQL Server's default collation makes that the same row as
    /// <c>Patient/p1</c>. Hence the literal comparison is tried first, and it keeps
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> for parity with fhir-server.
    /// </para>
    /// <para>
    /// A reference the parser resolves to a <em>different</em> server (<c>BaseUri</c> non-null, which
    /// covers both <c>ReferenceKind.External</c> and a base that did not parse as an absolute HTTP URL)
    /// names a different resource and must not match -- the index does not match it either. Nor does a
    /// reference the parser cannot shape at all (a contained <c>#id</c>, a bare <c>urn:uuid:</c>), which
    /// it reports with a null <c>ResourceType</c>.
    /// </para>
    /// </remarks>
    private static bool ResolvesToTarget(string reference, string target, IReferenceSearchValueParser referenceParser)
    {
        if (string.Equals(reference, target, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var parsed = referenceParser.Parse(reference);
        return parsed.ResourceType is not null
            && parsed.BaseUri is null
            && string.Equals($"{parsed.ResourceType}/{parsed.ResourceId}", target, StringComparison.OrdinalIgnoreCase);
    }
}
