// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>What resolving one resource's Bundle references did before it was indexed; persisted with its index.</summary>
/// <param name="Resolved">Each reference as written (a <c>urn:</c>, an absolute or a relative URL) and the <c>Type/id</c> it was indexed as.</param>
/// <param name="UnresolvedUrns">
/// The distinct <c>urn:</c> references that named no entry, which the type's <c>unresolved-bundle-reference</c> issue counts;
/// <c>references-unresolved</c> skips exactly these, so a reference is reported once.
/// </param>
public sealed record ReferenceResolution(IReadOnlyDictionary<string, string> Resolved, IReadOnlyList<string> UnresolvedUrns)
{
    /// <summary>Nothing resolved and nothing left unresolved.</summary>
    public static ReferenceResolution None { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal), []);
}
