using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a TokenToken composite to a single ParamSource over TokenTokenCompositeSearchParam --
/// components[0] compares Code1, components[1] compares Code2. Code-only case only, same as
/// TokenLoweringRule: System-qualified components (including System = string.Empty) and
/// text-only components (no Code) both throw rather than silently producing a wrong-scope or
/// always-false predicate.
/// </summary>
public static class TokenTokenLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context)
    {
        var table = SqlCatalog.Default.Table("TokenTokenCompositeSearchParam");
        var predicate = new Predicate.And(
            TokenColumnEquals(table, "Code1", (TokenSearchValue)components[0].Value, context),
            TokenColumnEquals(table, "Code2", (TokenSearchValue)components[1].Value, context));

        return new CteDefinition.ParamSource(table, context.SearchParamId(compositeParameter), predicate);
    }

    private static Predicate TokenColumnEquals(TableDescriptor table, string codeColumn, TokenSearchValue value, LeafContext context)
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

        var column = new SqlColumnRef(table.TableName, codeColumn);
        return new Predicate.Equal(column, context.Parameter(value.Code));
    }
}
