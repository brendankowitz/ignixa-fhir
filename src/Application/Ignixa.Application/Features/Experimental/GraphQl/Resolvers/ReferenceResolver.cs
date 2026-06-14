// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Application.Features.Experimental.GraphQl.Resolvers;

/// <summary>
/// Encapsulates the decision logic for resolving a FHIR <c>Reference.reference</c> string to the
/// referenced resource. Kept free of HotChocolate types so it can be unit tested directly; the
/// schema-type lambda in <c>FhirTypeModule</c> supplies the I/O and error-reporting callbacks.
/// </summary>
internal static class ReferenceResolver
{
    /// <summary>Outcome of a reference resolution attempt.</summary>
    internal enum Outcome
    {
        /// <summary>Reference shape is not resolvable (empty, contained, urn, or unparseable).</summary>
        NotSupported,

        /// <summary>Reference parsed, but the resolved type did not match the requested <c>type</c> filter.</summary>
        TypeMismatch,

        /// <summary>Reference parsed but the resource could not be loaded (load returned null).</summary>
        NotFound,

        /// <summary>Reference resolved to a resource.</summary>
        Resolved,
    }

    internal readonly record struct ReferenceResolution(Outcome Outcome, JsonElement? Resource);

    /// <summary>
    /// Resolves a reference string, applying the optional <paramref name="typeFilter"/> and using
    /// <paramref name="loadAsync"/> to fetch the resource. Returns both the outcome (so the caller
    /// can decide whether/how to report an error, honouring <paramref name="isOptional"/>) and the
    /// resolved resource when successful.
    /// </summary>
    internal static async Task<ReferenceResolution> ResolveAsync(
        string? reference,
        bool isOptional,
        string? typeFilter,
        IReferenceSearchValueParser parser,
        Func<ResourceKey, Task<JsonElement?>> loadAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(loadAsync);

        if (string.IsNullOrEmpty(reference)
            || reference.StartsWith('#')
            || reference.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
        {
            return new ReferenceResolution(Outcome.NotSupported, null);
        }

        var parsed = parser.Parse(reference);
        if (parsed.ResourceType is null)
            return new ReferenceResolution(Outcome.NotSupported, null);

        var key = new ResourceKey(parsed.ResourceType, parsed.ResourceId);

        if (typeFilter is not null
            && !string.Equals(key.ResourceType, typeFilter, StringComparison.Ordinal))
        {
            return new ReferenceResolution(Outcome.TypeMismatch, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var resource = await loadAsync(key).ConfigureAwait(false);

        return resource is null
            ? new ReferenceResolution(Outcome.NotFound, null)
            : new ReferenceResolution(Outcome.Resolved, resource);
    }

    /// <summary>
    /// Whether the resolver should surface an error for the given outcome. <c>optional: true</c>
    /// suppresses NotSupported/NotFound errors; type mismatches and successful resolutions never
    /// report. This is the single source of truth shared by the schema-type lambda and tests.
    /// </summary>
    internal static bool ShouldReportError(Outcome outcome, bool isOptional) =>
        !isOptional && outcome is Outcome.NotSupported or Outcome.NotFound;

    /// <summary>
    /// Builds the human-readable reason for an unsupported reference, mirroring the spec messages.
    /// Returns null for shapes that are not "unsupported" (i.e. successfully parsed references).
    /// </summary>
    internal static string? DescribeUnsupported(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
            return "the reference is empty";

        if (reference.StartsWith('#'))
            return "contained reference resolution is not supported";

        if (reference.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
            return "urn reference resolution is not supported";

        return "a resource type and id could not be parsed from the reference";
    }
}
