using System.Collections.Frozen;
using Ignixa.Search.Models;

namespace Ignixa.Search.Sql.Compilation;

/// <summary>
/// The enforced contract for <see cref="CompilationContext.Create"/>: every property of
/// <c>SearchOptions</c> is either mapped into a compilation input or explicitly not applicable with a
/// stated reason.
/// </summary>
/// <remarks>
/// Four properties have, one at a time, been added to <c>SearchOptions</c>, accepted by the compiler, and
/// never forwarded — each a control that looked live and silently did nothing. A test over these two
/// collections fails the build when a fifth is added and classified as neither.
/// </remarks>
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
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The properties that deliberately do not become compilation inputs, and why.</summary>
    public static FrozenDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(SearchOptions.MaxItemCount)] =
            "Callers transform it before a search runs — SearchResourcesHandler requests MaxItemCount + 1 to detect 'has more' — so forwarding it as Top would silently fight that transformation. Row capping is SearchPlanOptions.Top, Page, or OffsetPage.",
        [nameof(SearchOptions.ContinuationToken)] =
            "Decoding it into a keyset or OFFSET page is adapter logic in a different layer. The decoded result arrives as SearchPlanOptions.Page or OffsetPage.",
        [nameof(SearchOptions.Elements)] =
            "A serialization-time projection of the returned resource body, applied after the rows are read.",
        [nameof(SearchOptions.Total)] =
            "Bundle metadata. The compiler's only count concept is SearchPlanOptions.CountOnly, which the caller sets directly.",
        [nameof(SearchOptions.Summary)] =
            "A serialization-time projection, like Elements.",
        [nameof(SearchOptions.UnsupportedParams)] =
            "Builder output describing what it could not honour; it shapes the OperationOutcome, not the SQL.",
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
