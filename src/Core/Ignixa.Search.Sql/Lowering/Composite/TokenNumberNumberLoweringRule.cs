using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Composite;

/// <summary>
/// Lowers a TokenNumberNumber composite to a ParamSource over TokenNumberNumberCompositeSearchParam —
/// components[0] the token slot (Code1, code-only), components[1]/[2] the two number slots
/// (LowValue2/HighValue2, LowValue3/HighValue3), reusing <see cref="NumericRangeComparison"/> unchanged
/// against composite-table column names.
/// </summary>
internal static class TokenNumberNumberLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context,
        short? resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("TokenNumberNumberCompositeSearchParam");

        var tokenPredicate = TokenColumnEquality.Build(table, "SystemId1", "Code1", "CodeOverflow1", (TokenSearchValue)components[0].Value, context);
        var number1Predicate = NumberRangePredicate(table, "LowValue2", "HighValue2", components[1], context);
        var number2Predicate = NumberRangePredicate(table, "LowValue3", "HighValue3", components[2], context);

        var predicate = new Predicate.And(new Predicate.And(tokenPredicate, number1Predicate), number2Predicate);
        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(compositeParameter), predicate);
    }

    private static Predicate NumberRangePredicate(
        TableDescriptor table, string lowColumnName, string highColumnName, SearchParameterPredicateExpression component, LeafContext context)
    {
        var value = (NumberSearchValue)component.Value;
        var comparisonValue = value.Low ?? value.High
            ?? throw new NotSupportedException("NumberSearchValue has neither Low nor High set.");
        var lowColumn = new SqlColumnRef(table.TableName, lowColumnName);
        var highColumn = new SqlColumnRef(table.TableName, highColumnName);
        return NumericRangeComparison.Build(context, lowColumn, highColumn, component.Comparator, comparisonValue);
    }
}
