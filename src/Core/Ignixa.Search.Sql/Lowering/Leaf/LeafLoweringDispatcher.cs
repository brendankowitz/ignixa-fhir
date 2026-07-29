using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Lowering.Leaf;

/// <summary>
/// Dispatches a leaf predicate to its lowering rule by the runtime type of its ISearchValue. A composite
/// value has no leaf rule and throws — composites are lowered by <see cref="Composite.CompositeLoweringDispatcher"/>.
/// </summary>
internal static class LeafLoweringDispatcher
{
    /// <summary>The <see cref="Exception.Data"/> key a caught lowering failure carries its triggering predicate's <see cref="SourceSpan"/> under.</summary>
    internal const string SpanDataKey = "Ignixa.SourceSpan";

    /// <summary>
    /// The <see cref="Exception.Data"/> key a caught lowering failure carries its triggering
    /// <see cref="SearchParameterInfo"/> under. This, not the span, is what attributes a failure
    /// to a parameter: spans repeat across parameters (two same-length values share one), so span alone
    /// would smear one parameter's failure across its innocent neighbours.
    /// </summary>
    internal const string ParameterDataKey = "Ignixa.SearchParameter";

    /// <summary>
    /// Lowers a leaf predicate, attributing any lowering failure to the predicate's parameter on the way out.
    /// </summary>
    /// <remarks>
    /// Both exception types the compiler treats as a lowering failure are enriched. A
    /// <see cref="NotSupportedException"/> is an unimplemented shape; a <see cref="KeyNotFoundException"/>
    /// is a <see cref="Symbols.SymbolTable"/> miss, which means Resolve's tree-walk never collected a
    /// symbol this predicate needs. The second is a compiler invariant violation rather than a property of
    /// the query, but it is still raised by one identifiable parameter, and leaving it unattributed sends
    /// it down the unattributed early-return in the trace's failure recorder — where a trace can say only
    /// that the search failed, not which parameter did it, which is the one thing needed to find the gap.
    /// </remarks>
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, LeafContext context, short? resourceTypeId)
    {
        try
        {
            return LowerCore(predicate, context, resourceTypeId);
        }
        catch (Exception ex) when (IsUnattributedLoweringFailure(ex))
        {
            Enrich(ex, predicate.Parameter, predicate.Span);
            throw;
        }
    }

    /// <summary>Whether an in-flight exception is a lowering failure no inner frame has already attributed.</summary>
    internal static bool IsUnattributedLoweringFailure(Exception ex)
        => ex is NotSupportedException or KeyNotFoundException && !ex.Data.Contains(ParameterDataKey);

    /// <summary>Attaches the failing parameter, and its span when it has one, to an in-flight exception for the trace to attribute later.</summary>
    internal static void Enrich(Exception ex, SearchParameterInfo parameter, SourceSpan? span)
    {
        ex.Data[ParameterDataKey] = parameter;
        if (span is { } value)
        {
            ex.Data[SpanDataKey] = value;
        }
    }

    private static CteDefinition.ParamSource LowerCore(SearchParameterPredicateExpression predicate, LeafContext context, short? resourceTypeId) => predicate.Value switch
    {
        StringSearchValue s => StringLoweringRule.Lower(predicate, s, context, resourceTypeId),
        TokenSearchValue t => TokenLoweringRule.Lower(predicate, t, context, resourceTypeId),
        ReferenceSearchValue r => ReferenceLoweringRule.Lower(predicate, r, context, resourceTypeId),
        UriSearchValue u => UriLoweringRule.Lower(predicate, u, context, resourceTypeId),
        NumberSearchValue n => NumberLoweringRule.Lower(predicate, n, context, resourceTypeId),
        QuantitySearchValue q => QuantityLoweringRule.Lower(predicate, q, context, resourceTypeId),
        DateTimeSearchValue d => DateTimeLoweringRule.Lower(predicate, d, context, resourceTypeId),
        _ => throw new NotSupportedException(
            $"No lowering rule for {predicate.Value.GetType().Name} -- composites are out of scope for this plan."),
    };
}
