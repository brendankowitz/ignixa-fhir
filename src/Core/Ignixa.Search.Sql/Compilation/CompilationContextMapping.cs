using System.Collections.Frozen;
using Ignixa.Search.Models;

namespace Ignixa.Search.Sql.Compilation;

/// <summary>
/// Build-failing completeness contract for <see cref="CompilationContext.Create"/>: every
/// <c>SearchOptions</c> property must be classified <see cref="Mapped"/> or <see cref="NotApplicable"/>. A
/// test over these two collections fails the build when a new property is neither — the shape of past
/// defects where a live-looking control (once the fail-open <c>AllowedResourceTypes</c>) was silently never
/// forwarded. Do not weaken it.
/// </summary>
internal static class CompilationContextMapping
{
    /// <summary>The properties <see cref="CompilationContext.Create"/> reads.</summary>
    public static FrozenSet<string> Mapped { get; } = new[]
    {
        nameof(SearchOptions.Expression),
        nameof(SearchOptions.Sort),
        nameof(SearchOptions.Include),
        nameof(SearchOptions.RevInclude),
        nameof(SearchOptions.ResourceTypes),
        nameof(SearchOptions.StartSurrogateId),
        nameof(SearchOptions.EndSurrogateId),
        nameof(SearchOptions.ResourceVersionTypes),
        nameof(SearchOptions.AccessConstraints),
        nameof(SearchOptions.AllowedResourceTypes),
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The properties that deliberately do not become compilation inputs, and why.</summary>
    public static FrozenDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(SearchOptions.MaxItemCount)] =
            "A page size, not a row cap: the adapter layer pairs it with ProbeExtraRow and a decoded continuation token to build the OffsetSpec on ResultShape.Matches. Forwarding it as a cap as well would apply the same number twice. Row capping is SearchPaging.Keyset.Top on ResultShape.Matches.",
        [nameof(SearchOptions.ProbeExtraRow)] =
            "Reaches the compiler as OffsetSpec.ProbeExtraRow, built by the adapter layer alongside MaxItemCount. The AST models the over-fetch structurally, so there is nothing left for a separate compilation input to say.",
        [nameof(SearchOptions.ContinuationToken)] =
            "Decoding it into a keyset or OFFSET page is adapter logic in a different layer. The decoded result arrives as the SearchPaging on ResultShape.Matches.",
        [nameof(SearchOptions.Elements)] =
            "A serialization-time projection of the returned resource body, applied after the rows are read.",
        [nameof(SearchOptions.Total)] =
            "Bundle metadata. The compiler's only count concept is the ResultShape.Count result shape, which the caller sets directly.",
        [nameof(SearchOptions.Summary)] =
            "A serialization-time projection, like Elements.",
        [nameof(SearchOptions.UnsupportedParams)] =
            "Builder output describing what it could not honour; it shapes the OperationOutcome, not the SQL.",
        [nameof(SearchOptions.UnsupportedModifierParams)] =
            "Builder output, like UnsupportedParams: the subset R4 says SHALL be rejected rather than ignored. It decides an HTTP status at the API boundary, never a CTE.",
        [nameof(SearchOptions.BundleIssues)] =
            "Builder output, like UnsupportedParams.",
        [nameof(SearchOptions.ResourceType)] =
            "Superseded by the targetResourceType argument, which is normalized once in CompilationContext.Create so every stage observes the same value.",
        [nameof(SearchOptions.IncludesMaxItemCount)] =
            "The $includes operation's page size, applied by the caller. The compiler's per-stage cap is SearchPlanOptions.IncludeLimit.",
        [nameof(SearchOptions.IncludesContinuationToken)] =
            "Decoded by the adapter layer, like ContinuationToken.",
    }.ToFrozenDictionary(StringComparer.Ordinal);
}
