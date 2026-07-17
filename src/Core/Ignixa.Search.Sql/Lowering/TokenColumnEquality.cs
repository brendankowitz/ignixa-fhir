using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the code-only equality predicate shared by every composite type's Token slot (TokenToken,
/// TokenNumberNumber, TokenString, TokenQuantity, TokenDateTime, ReferenceToken all have one) --
/// identical semantics to TokenLoweringRule: System-qualified components (including System =
/// string.Empty) and text-only components (no Code) both throw rather than silently producing a
/// wrong-scope or always-false predicate.
/// </summary>
internal static class TokenColumnEquality
{
    public static Predicate Build(TableDescriptor table, string codeColumn, TokenSearchValue value, LeafContext context)
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
