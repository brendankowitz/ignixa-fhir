// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>What resolving one resource's Bundle references did before it was indexed; persisted with its index.</summary>
/// <param name="Resolved">
/// Each reference as written (a <c>urn:</c>, an absolute or a relative URL, or a conditional <c>Type?identifier=...</c>) and
/// the <c>Type/id</c> it was indexed as.
/// </param>
/// <param name="UnresolvedUrns">
/// The distinct <c>urn:</c> references that named no entry, which the type's <c>unresolved-bundle-reference</c> issue counts;
/// <c>references-unresolved</c> skips exactly these, so a reference is reported once.
/// </param>
/// <param name="UnresolvedConditionals">
/// The distinct conditional references left as written, with why; counted by the same issue and skipped the same way.
/// </param>
public sealed record ReferenceResolution(
    IReadOnlyDictionary<string, string> Resolved,
    IReadOnlyList<string> UnresolvedUrns,
    IReadOnlyList<UnresolvedConditional> UnresolvedConditionals)
{
    /// <summary>Nothing resolved and nothing left unresolved.</summary>
    public static ReferenceResolution None { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal), [], []);

    /// <summary>
    /// Whether <paramref name="written"/>, the text an index entry was written as
    /// (<see cref="BundleReferenceResolver.WrittenOf"/>), is one the type's index reported as unresolved: a <c>urn:</c> as is, a
    /// conditional reference as is or cut short by the reference parser (so a prefix of the reported one).
    /// </summary>
    public bool ReportedUnresolved(string written) =>
        UnresolvedUrns.Contains(written, StringComparer.Ordinal)
        || (ConditionalReferenceResolver.IsConditional(written)
            && UnresolvedConditionals.Any(conditional => conditional.Reference.StartsWith(written, StringComparison.Ordinal)));
}
