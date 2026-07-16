using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a String search value to a ParamSource over StringSearchParam. Values within the inline
/// width compare against Text; values beyond it compare against TextOverflow, which holds the whole
/// value per this plan's task 1 (matching fhir-server's convention).
///
/// This single-column choice is NOT correct for every modifier once a row has overflowed (Text ==
/// TextOverflow's first-256-char prefix, not its full value): a `:contains` search value within the
/// inline width can never be expressed correctly against `Text` alone, because a true substring match
/// that only occurs at or after character 256 of an overflowed row's real value is invisible to
/// `Text LIKE '%x%'` -- this rule throws NotSupportedException for that case rather than silently
/// missing matches. An `:exact` search value of exactly the inline width has the same problem in the
/// other direction: `Text = @p` can false-positive match an overflowed row whose true value merely
/// shares that 256-char prefix -- this rule throws for that case too. The default/`StartsWith` case
/// is safe in both the overflow and non-overflow case: a true "starts with X" match (X within the
/// inline width) is always captured in the first 256 characters, so Text alone is sufficient.
/// Fixing `:contains` and the exact-at-256 case for real would require either extending Predicate with
/// Or/IsNull to search both Text and TextOverflow, or fhir-server's Text-prefix-seek technique; both
/// are documented follow-ups, not implemented here.
/// </summary>
public static class StringLoweringRule
{
    private const string CaseInsensitiveCollation = "Latin1_General_100_CI_AI";
    private const string CaseSensitiveCollation = "Latin1_General_100_CS_AS";

    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, StringSearchValue value, LeafContext context)
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

        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), p);
    }
}
