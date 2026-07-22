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
/// <c>:contains</c> within the inline width searches both columns:
/// <c>Or(And(IsNull(TextOverflow), Like(Text, …, Contains)), Like(TextOverflow, …, Contains))</c>.
/// Non-overflowed rows are matched via Text (the IsNull guard ensures TextOverflow is absent); overflowed
/// rows are matched via the complete TextOverflow column. For values exceeding the inline width,
/// <c>:contains</c> targets TextOverflow only — such a substring can only exist in a value that itself
/// exceeds the inline width, so TextOverflow is guaranteed populated.
/// The default StartsWith case is always safe, since a prefix within the inline width is fully captured
/// in Text.
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

        Predicate p = (exact, contains, usesTextColumn) switch
        {
            // :exact with value fitting within inline width: IsNull guard excludes overflowed rows
            // whose truncated Text prefix would otherwise false-positive match the search value.
            (true, _, true) => new Predicate.And(
                new Predicate.IsNull(overflowColumn),
                new Predicate.Equal(textColumn, context.Parameter(value.String), CaseSensitiveCollation)),
            // :exact with value exceeding inline width: TextOverflow holds the complete stored value.
            (true, _, false) => new Predicate.Equal(overflowColumn, context.Parameter(value.String), CaseSensitiveCollation),
            // :contains with value within inline width: search both Text (for non-overflowed rows) and
            // TextOverflow (for overflowed rows). The IsNull guard on the Text branch excludes overflowed
            // rows whose truncated Text could yield false negatives (the substring might only exist past
            // offset 256). Each Like uses a separate parameter ref so emission produces @p0 then @p1.
            (_, true, true) => new Predicate.Or(
                new Predicate.And(
                    new Predicate.IsNull(overflowColumn),
                    new Predicate.Like(textColumn, context.Parameter(value.String), LikeMatch.Contains, CaseInsensitiveCollation)),
                new Predicate.Like(overflowColumn, context.Parameter(value.String), LikeMatch.Contains, CaseInsensitiveCollation)),
            // :contains with value exceeding inline width: the substring can only exist in a stored value
            // that itself exceeded the inline width, so TextOverflow is guaranteed populated.
            (_, true, false) => new Predicate.Like(overflowColumn, context.Parameter(value.String), LikeMatch.Contains, CaseInsensitiveCollation),
            // Default StartsWith: Text for inline values, TextOverflow for longer ones.
            _ => new Predicate.Like(usesTextColumn ? textColumn : overflowColumn, context.Parameter(value.String), LikeMatch.StartsWith, CaseInsensitiveCollation),
        };

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), p);
    }
}
