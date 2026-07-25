namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The kind tokens carried by <see cref="PlanExplainRow.Kind"/> — which <c>CteDefinition</c> case (or
/// which non-CTE plan section) produced the row.
/// </summary>
/// <remarks>
/// <see cref="PlanExplainer"/> already switches over the definition's case to format the body; the kind is
/// that same discrimination kept as data instead of discarded once the string is built. Without it a
/// consumer has to recover the case by prefix-matching formatted prose (<c>"Intersect("</c>,
/// <c>"ChainJoin("</c>), which turns a display-text change into a downstream break.
/// <para>
/// Tokens, not an enum, for the same reason as <see cref="Builders.SqlRangeKind"/>: these are consumed by
/// renderers across a wire.
/// </para>
/// </remarks>
public static class PlanRowKind
{
    public const string ParamSource = "paramSource";

    public const string Intersect = "intersect";

    public const string Union = "union";

    public const string ResourceSource = "resourceSource";

    public const string Except = "except";

    public const string ChainJoin = "chainJoin";

    public const string CompartmentSource = "compartmentSource";

    public const string NotReferencedSource = "notReferencedSource";

    public const string MultiTypeResourceSource = "multiTypeResourceSource";

    public const string IncludeStage = "includeStage";

    public const string SortSpec = "sortSpec";

    public const string PageSpec = "pageSpec";

    public const string CountOnly = "countOnly";
}
