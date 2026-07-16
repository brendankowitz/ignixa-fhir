using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a TokenNumberNumber composite to a single ParamSource over
/// TokenNumberNumberCompositeSearchParam -- components[0] is the token slot (Code1, code-only,
/// same throw rules as TokenLoweringRule), components[1]/[2] are the two number slots (LowValue2/
/// HighValue2, LowValue3/HighValue3), reusing NumericRangeComparison unchanged -- same range
/// semantics as NumberLoweringRule, just against composite-table column names.
/// </summary>
public static class TokenNumberNumberLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context)
    {
        var table = SqlCatalog.Default.Table("TokenNumberNumberCompositeSearchParam");

        var tokenPredicate = TokenColumnEquals(table, (TokenSearchValue)components[0].Value, context);
        var number1Predicate = NumberRangePredicate(table, "LowValue2", "HighValue2", components[1], context);
        var number2Predicate = NumberRangePredicate(table, "LowValue3", "HighValue3", components[2], context);

        var predicate = new Predicate.And(new Predicate.And(tokenPredicate, number1Predicate), number2Predicate);
        return new CteDefinition.ParamSource(table, context.SearchParamId(compositeParameter), predicate);
    }

    private static Predicate TokenColumnEquals(TableDescriptor table, TokenSearchValue value, LeafContext context)
    {
        if (value.System is not null)
        {
            throw new NotSupportedException(
                "System-qualified token components are not supported yet -- same SystemId resolution gap as " +
                "TokenLoweringRule (ISymbolResolver has no SystemId lookup). This includes System = string.Empty " +
                "(\"|code\" syntax, meaning system must be absent), which this rule cannot express either.");
        }

        if (string.IsNullOrEmpty(value.Code))
        {
            throw new NotSupportedException(
                "This rule only supports code-bearing token components -- text-only components (Code is null/empty) " +
                "are not supported yet.");
        }

        var column = new SqlColumnRef(table.TableName, "Code1");
        return new Predicate.Equal(column, context.Parameter(value.Code));
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
