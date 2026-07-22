using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a String search value to a ParamSource over StringSearchParam. Values within the inline width
/// compare against Text; longer values compare against TextOverflow, which holds the whole value.
/// <para>
/// Comparing a single column is not correct for every modifier once a row has overflowed, because Text
/// then holds only the first 256 characters. <c>:exact</c> for a value at or within the inline width
/// returns <c>IsNull(TextOverflow) AND Text = value</c> — the IsNull guard excludes overflowed rows
/// whose truncated Text prefix would otherwise false-positive match. For values longer than the inline
/// width, <c>:exact</c> compares TextOverflow directly, which holds the complete stored value.
/// <c>:contains</c> within the inline width would miss a substring that occurs only past character 256
/// in an overflowed row, so it is not yet supported and throws until the IR can express the dual-column
/// Or/IsNull shape. The default StartsWith case is always safe, since a prefix within the inline width
/// is fully captured in Text.
/// </para>
/// </summary>
public static class StringLoweringRule
{
    private const string CaseInsensitiveCollation = "Latin1_General_100_CI_AI";
    private const string CaseSensitiveCollation = "Latin1_General_100_CS_AS";

    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, StringSearchValue value, LeafContext context, short resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var inlineWidth = table.Column("Text").MaxLength
            ?? throw new InvalidOperationException("StringSearchParam.Text has no MaxLength in SqlCatalog.");

        var textColumn = new SqlColumnRef(table.TableName, "Text");
        var overflowColumn = new SqlColumnRef(table.TableName, "TextOverflow");
        var usesTextColumn = value.String.Length <= inlineWidth;

        var exact = predicate.Modifier?.SearchModifierCode == SearchModifierCode.Exact;
        var contains = predicate.Modifier?.SearchModifierCode == SearchModifierCode.Contains;

        if (usesTextColumn && contains)
        {
            throw new NotSupportedException(
                $"':contains' cannot be expressed correctly against StringSearchParam.Text for a value within the inline " +
                $"width ({inlineWidth} chars): an overflowed row's Text holds only the first {inlineWidth} characters of its " +
                "true value, so a substring match that exists only at or after that offset in TextOverflow would be silently " +
                "missed. Handling this case needs an Or(And(IsNull(TextOverflow), Like(Text, …)), Like(TextOverflow, …)) shape.");
        }

        Predicate p = (exact, usesTextColumn) switch
        {
            // :exact with value fitting within inline width: IsNull guard excludes overflowed rows
            // whose truncated Text prefix would otherwise false-positive match the search value.
            (true, true) => new Predicate.And(
                new Predicate.IsNull(overflowColumn),
                new Predicate.Equal(textColumn, context.Parameter(value.String), CaseSensitiveCollation)),
            // :exact with value exceeding inline width: TextOverflow holds the complete stored value.
            (true, false) => new Predicate.Equal(overflowColumn, context.Parameter(value.String), CaseSensitiveCollation),
            // :contains with value exceeding inline width (short :contains is thrown above).
            (false, _) when contains => new Predicate.Like(overflowColumn, context.Parameter(value.String), LikeMatch.Contains, CaseInsensitiveCollation),
            // Default StartsWith: Text for inline values, TextOverflow for longer ones.
            _ => new Predicate.Like(usesTextColumn ? textColumn : overflowColumn, context.Parameter(value.String), LikeMatch.StartsWith, CaseInsensitiveCollation),
        };

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), p);
    }
}
