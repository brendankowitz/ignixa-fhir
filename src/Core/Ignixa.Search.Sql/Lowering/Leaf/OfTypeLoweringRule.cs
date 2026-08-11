using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Lowers an <c>:of-type</c> identifier search to a single ParamSource over TokenSearchParam, conjoining
/// the identifier value (Code/CodeOverflow) with the Identifier.type code and, when the search names one,
/// the Identifier.type system.
/// <para>
/// One source with conjoined predicates -- not an intersection of three sources -- is the whole point: the
/// three conditions must hold on the <em>same</em> TokenSearchParam row, because a row is one Identifier.
/// A patient carrying MR|12345 and SS|67890 satisfies "some row has type SS" and "some row has value 12345"
/// separately, yet <c>:of-type=…|SS|12345</c> must not match them.
/// </para>
/// </summary>
internal static class OfTypeLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, OfTypeTokenSearchValue value, LeafContext context, short? resourceTypeId)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(context);

        var table = SqlCatalog.Default.Table("TokenSearchParam");

        return new CteDefinition.ParamSource(
            table,
            resourceTypeId,
            context.SearchParamId(predicate.Parameter),
            BuildPredicate(table, value, context));
    }

    private static Predicate BuildPredicate(TableDescriptor table, OfTypeTokenSearchValue value, LeafContext context)
    {
        var valueEquality = TokenColumnEquality.CodeEquality(table, "Code", "CodeOverflow", value.IdentifierValue, context)
            ?? throw new NotSupportedException(
                "An :of-type search carried no identifier value -- a type alone selects every identifier of that type, which is not what the modifier means.");

        Predicate typed = new Predicate.And(
            new Predicate.Equal(new SqlColumnRef(table.TableName, "IdentifierTypeCode"), context.Parameter(value.TypeCode)),
            valueEquality);

        // The |code|value form omits the type system deliberately (FHIR allows it), so IdentifierTypeSystemId
        // goes unconstrained rather than being matched against NULL: rows written before the system was known
        // carry NULL there and still have the right type code.
        if (value.TypeSystem is not { Length: > 0 } typeSystem)
        {
            return typed;
        }

        return context.SystemId(typeSystem) is { } typeSystemId
            ? new Predicate.And(
                new Predicate.Equal(new SqlColumnRef(table.TableName, "IdentifierTypeSystemId"), context.Parameter(typeSystemId)),
                typed)
            : new Predicate.False($"No resource uses the identifier type system '{typeSystem}'.");
    }
}
