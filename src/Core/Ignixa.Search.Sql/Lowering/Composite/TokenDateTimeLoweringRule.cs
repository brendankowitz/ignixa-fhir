using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Composite;

/// <summary>
/// Lowers a TokenDateTime composite to a ParamSource over TokenDateTimeCompositeSearchParam — components[0]
/// the token slot (Code1), components[1] the datetime slot (StartDateTime2/EndDateTime2), reusing
/// <see cref="DateTimeRangeComparison"/> unchanged against composite-table column names.
/// </summary>
internal static class TokenDateTimeLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context,
        short? resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("TokenDateTimeCompositeSearchParam");

        var tokenPredicate = TokenColumnEquality.Build(table, "SystemId1", "Code1", "CodeOverflow1", (TokenSearchValue)components[0].Value, context);

        var dateComponent = components[1];
        var dateValue = (DateTimeSearchValue)dateComponent.Value;
        var startColumn = new SqlColumnRef(table.TableName, "StartDateTime2");
        var endColumn = new SqlColumnRef(table.TableName, "EndDateTime2");
        var datePredicate = DateTimeRangeComparison.Build(context, startColumn, endColumn, dateComponent.Comparator, dateValue);

        var predicate = new Predicate.And(tokenPredicate, datePredicate);
        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(compositeParameter), predicate);
    }
}
