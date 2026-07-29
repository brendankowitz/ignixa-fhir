using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers a String search value to a ParamSource over StringSearchParam. Within-width values compare
/// against Text; longer values against TextOverflow (which holds the whole value). Since an overflowed
/// row's Text holds only its first 256 chars, within-width :exact/:contains add an <c>IsNull(TextOverflow)</c>
/// guard so a truncated Text prefix can't false-positive match. StartsWith is always safe.
/// </summary>
internal static class StringLoweringRule
{
    private const string CaseInsensitiveCollation = "Latin1_General_100_CI_AI";
    private const string CaseSensitiveCollation = "Latin1_General_100_CS_AS";

    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, StringSearchValue value, LeafContext context, short? resourceTypeId)
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
            (true, _, true) => new Predicate.And(
                new Predicate.IsNull(overflowColumn),
                new Predicate.Equal(textColumn, context.Parameter(value.String), CaseSensitiveCollation)),
            (true, _, false) => new Predicate.Equal(overflowColumn, context.Parameter(value.String), CaseSensitiveCollation),
            // Each Like takes its own parameter ref, so emission binds @p0 then @p1 rather than reusing one.
            (_, true, true) => new Predicate.Or(
                new Predicate.And(
                    new Predicate.IsNull(overflowColumn),
                    new Predicate.Like(textColumn, context.Parameter(value.String), LikeMatch.Contains, CaseInsensitiveCollation)),
                new Predicate.Like(overflowColumn, context.Parameter(value.String), LikeMatch.Contains, CaseInsensitiveCollation)),
            (_, true, false) => new Predicate.Like(overflowColumn, context.Parameter(value.String), LikeMatch.Contains, CaseInsensitiveCollation),
            _ => new Predicate.Like(usesTextColumn ? textColumn : overflowColumn, context.Parameter(value.String), LikeMatch.StartsWith, CaseInsensitiveCollation),
        };

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), p);
    }
}
