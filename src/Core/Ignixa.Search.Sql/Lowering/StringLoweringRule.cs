using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a String search value to a ParamSource over StringSearchParam. Values within the inline
/// width match Text directly; values beyond it match TextOverflow directly -- correct now that this
/// plan's task 1 makes TextOverflow hold the whole value, matching fhir-server's convention.
/// fhir-server also adds a redundant Text-prefix-seek check for the overflow case as a performance
/// optimization (its own index can still be used); this rule is correct without that optimization,
/// which is a documented follow-up, not required here.
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

        var column = new SqlColumnRef(table.TableName, value.String.Length > inlineWidth ? "TextOverflow" : "Text");

        var exact = predicate.Modifier?.SearchModifierCode == SearchModifierCode.Exact;
        var contains = predicate.Modifier?.SearchModifierCode == SearchModifierCode.Contains;
        var collation = exact ? CaseSensitiveCollation : CaseInsensitiveCollation;

        Predicate p = (exact, contains) switch
        {
            (true, _) => new Predicate.Equal(column, context.Parameter(value.String), collation),
            (false, true) => new Predicate.Like(column, context.Parameter(value.String), LikeMatch.Contains, collation),
            _ => new Predicate.Equal(column, context.Parameter(value.String), collation),
        };

        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), p);
    }
}
