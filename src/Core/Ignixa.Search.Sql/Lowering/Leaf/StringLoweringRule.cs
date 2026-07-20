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
/// then holds only the first 256 characters. <c>:contains</c> within the inline width would miss a
/// substring that occurs only past character 256, and <c>:exact</c> at exactly the inline width could
/// false-positive on a longer value sharing the same 256-char prefix — both throw rather than return
/// wrong rows. The default StartsWith case is always safe, since a prefix within the inline width is
/// fully captured in Text. Handling the two thrown cases would need the IR to search both columns.
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

        var usesTextColumn = value.String.Length <= inlineWidth;
        var column = new SqlColumnRef(table.TableName, usesTextColumn ? "Text" : "TextOverflow");

        var exact = predicate.Modifier?.SearchModifierCode == SearchModifierCode.Exact;
        var contains = predicate.Modifier?.SearchModifierCode == SearchModifierCode.Contains;

        if (usesTextColumn && contains)
        {
            throw new NotSupportedException(
                $"':contains' cannot be expressed correctly against StringSearchParam.Text for a value within the inline " +
                $"width ({inlineWidth} chars): an overflowed row's Text holds only the first {inlineWidth} characters of its " +
                "true value, so a substring match that exists only at or after that offset in TextOverflow would be silently " +
                "missed. The predicate IR has no Or/IsNull to search both columns, so this cannot be expressed correctly yet.");
        }

        if (usesTextColumn && exact && value.String.Length == inlineWidth)
        {
            throw new NotSupportedException(
                $"':exact' cannot be expressed correctly against StringSearchParam.Text for a value exactly {inlineWidth} " +
                $"characters long: an overflowed row's Text is always exactly {inlineWidth} characters (a truncated prefix " +
                "of its true value), so 'Text = @p' could false-positive match a row whose true value is longer but shares " +
                $"the same {inlineWidth}-char prefix as the search value.");
        }

        var collation = exact ? CaseSensitiveCollation : CaseInsensitiveCollation;

        Predicate p = (exact, contains) switch
        {
            (true, _) => new Predicate.Equal(column, context.Parameter(value.String), collation),
            (false, true) => new Predicate.Like(column, context.Parameter(value.String), LikeMatch.Contains, collation),
            _ => new Predicate.Like(column, context.Parameter(value.String), LikeMatch.StartsWith, collation),
        };

        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), p);
    }
}
