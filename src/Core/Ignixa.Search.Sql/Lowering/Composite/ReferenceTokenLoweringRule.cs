using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering.Composite;

/// <summary>
/// Lowers a ReferenceToken composite to a ParamSource over ReferenceTokenCompositeSearchParam, routing the reference
/// slot through <see cref="ReferenceColumnEquality"/> and the token slot through <see cref="TokenColumnEquality"/>.
/// </summary>
/// <remarks>
/// <para>
/// The table has one column set per kind (reference first, token second) whatever order a definition declares its
/// components in - <c>DocumentReference-relationship</c> declares reference then token, others token then reference -
/// and <c>RefTokenCompositeRowGenerator</c> fills it by value type. A query, though, is positional: a composite value
/// is a tuple joined with <c>$</c>, and "the matching parameter of each component in such a sequence is documented in
/// the definition of the parameter" (R4 §3.1.1.4.17; R5 §3.2.1.5.16, https://hl7.org/fhir/R5/search.html#composite),
/// so the n-th value is matched by the n-th declared component. Each slot therefore takes the value given at the
/// position whose declared type is that slot's kind.
/// </para>
/// <para>
/// The binder types a component value by its shape where the shape disagrees with the declaration (a
/// <c>Type/id</c>-looking value becomes a reference, a value with <c>|</c> a token), so a query that swaps the two
/// values arrives with a reference at the token position. Matching it by value type would answer the swapped query as
/// if it were the right one; the index can never hold a reference at the token position, so it matches nothing
/// (<see cref="Predicate.False"/>), as the in-memory matcher answers.
/// </para>
/// </remarks>
internal static class ReferenceTokenLoweringRule
{
    /// <summary>Lowers <paramref name="components"/>, the composite's predicates in position order (the dispatcher sorts them).</summary>
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context,
        short? resourceTypeId)
    {
        var table = SqlCatalog.Default.Table("ReferenceTokenCompositeSearchParam");
        short searchParamId = context.SearchParamId(compositeParameter);

        SearchParameterPredicateExpression referenceComponent = components[DeclaredPosition(compositeParameter, SearchParamType.Reference, components.Count)];
        SearchParameterPredicateExpression tokenComponent = components[DeclaredPosition(compositeParameter, SearchParamType.Token, components.Count)];
        if (referenceComponent.Value is not ReferenceSearchValue reference || tokenComponent.Value is not TokenSearchValue token)
        {
            return new CteDefinition.ParamSource(
                table,
                resourceTypeId,
                searchParamId,
                new Predicate.False($"The values of composite '{compositeParameter.Code}' are not in its declared component order (a reference where the token belongs), so no indexed value can match."));
        }

        var referencePredicate = ReferenceColumnEquality.Build(
            table, "BaseUri1", "ReferenceResourceTypeId1", "ReferenceResourceId1",
            reference, context, referenceComponent.Parameter);
        var tokenPredicate = TokenColumnEquality.Build(table, "SystemId2", "Code2", "CodeOverflow2", token, context);

        var predicate = new Predicate.And(referencePredicate, tokenPredicate);
        return new CteDefinition.ParamSource(table, resourceTypeId, searchParamId, predicate);
    }

    private static int DeclaredPosition(SearchParameterInfo compositeParameter, SearchParamType kind, int supplied)
    {
        List<int> positions = [.. Enumerable.Range(0, Math.Min(supplied, compositeParameter.Component.Count))
            .Where(index => compositeParameter.Component[index].ResolvedSearchParameter?.Type == kind)];

        return positions.Count == 1
            ? positions[0]
            : throw new NotSupportedException(
                $"ReferenceToken composite '{compositeParameter.Code}' must declare exactly one {kind} component among the {supplied} supplied, but declares {positions.Count}.");
    }
}
