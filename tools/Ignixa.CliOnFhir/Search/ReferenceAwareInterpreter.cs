// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// <see cref="SearchQueryInterpreter"/> plus evaluation of reference search parameters (<c>subject=Patient/p1</c>), which
/// the interpreter itself cannot do: it lowers a reference to string comparisons on the reference's parts and then
/// throws <see cref="NotImplementedException"/> for any parameter of type Reference
/// (see the pre-existing-gap test in <c>test/Ignixa.Application.Tests/Search/InMemory/SearchQueryInterpreterPredicateTests.cs</c>).
/// Everything else is delegated to the interpreter. Its recursive visits (<see cref="VisitSearchParameter"/>,
/// <see cref="VisitMultiary"/>, <see cref="VisitNotExpression"/>, <see cref="VisitUnion"/>) are copied from
/// <c>src/Core/Ignixa.Search/InMemory/SearchQueryInterpreter.cs</c> so that they recurse through this class, not the inner one.
/// </summary>
public sealed class ReferenceAwareInterpreter : IExpressionVisitorWithInitialContext<SearchQueryInterpreter.Context, SearchPredicate>
{
    private readonly SearchQueryInterpreter _inner = new();

    public SearchQueryInterpreter.Context InitialContext => _inner.InitialContext;

    public SearchPredicate VisitSearchParameterPredicate(SearchParameterPredicateExpression expression, SearchQueryInterpreter.Context context)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return expression.Parameter.Type == SearchParamType.Reference && expression.Value is ReferenceSearchValue query
            ? MatchReference(expression.Parameter.Name, query)
            : _inner.VisitSearchParameterPredicate(expression, context);
    }

    /// <summary>
    /// Same matching rules the SQL data layer lowers a reference value to
    /// (<c>SearchValueExpressionBuilderHelper.Visit(ReferenceSearchValue)</c>): a base URI, when the query has one,
    /// must match; a query without a type matches by id alone; an internal query must match a reference with no base URI.
    /// </summary>
    private static SearchPredicate MatchReference(string parameterName, ReferenceSearchValue query) =>
        input => input.Where(resource => resource.Index.Any(entry =>
            entry.SearchParameter.Name == parameterName
            && entry.Value is ReferenceSearchValue indexed
            && Matches(query, indexed)));

    private static bool Matches(ReferenceSearchValue query, ReferenceSearchValue indexed)
    {
        if (query.BaseUri is not null)
        {
            return query.BaseUri == indexed.BaseUri && IsSameTarget(query, indexed);
        }

        if (query.ResourceType is null)
        {
            return query.ResourceId == indexed.ResourceId;
        }

        return (query.Kind != ReferenceKind.Internal || indexed.BaseUri is null) && IsSameTarget(query, indexed);
    }

    private static bool IsSameTarget(ReferenceSearchValue query, ReferenceSearchValue indexed) =>
        query.ResourceType == indexed.ResourceType && query.ResourceId == indexed.ResourceId;

    public SearchPredicate VisitSearchParameter(SearchParameterExpression expression, SearchQueryInterpreter.Context context)
    {
        ArgumentNullException.ThrowIfNull(expression);

        SearchQueryInterpreter.Context named = context.WithParameterName(expression.Parameter.Name);
        return input => expression.Expression.AcceptVisitor(this, named)(input);
    }

    public SearchPredicate VisitMultiary(MultiaryExpression expression, SearchQueryInterpreter.Context context)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return expression.Expressions
            .Select(x => x.AcceptVisitor(this, context))
            .Aggregate((x, y) => expression.MultiaryOperation switch
            {
                MultiaryOperator.And => p => x(p).Intersect(y(p)),
                MultiaryOperator.Or => p => x(p).Union(y(p)),
                _ => throw new NotSupportedException($"MultiaryOperator {expression.MultiaryOperation} is not supported."),
            });
    }

    public SearchPredicate VisitNotExpression(NotExpression expression, SearchQueryInterpreter.Context context)
    {
        ArgumentNullException.ThrowIfNull(expression);

        SearchPredicate inner = expression.Expression.AcceptVisitor(this, context);
        return input => input.Except(inner(input));
    }

    public SearchPredicate VisitUnion(UnionExpression expression, SearchQueryInterpreter.Context context)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return expression.Expressions
            .Select(x => x.AcceptVisitor(this, context))
            .Aggregate((x, y) => p => x(p).Union(y(p)));
    }

    public SearchPredicate VisitBinary(BinaryExpression expression, SearchQueryInterpreter.Context context) => _inner.VisitBinary(expression, context);

    public SearchPredicate VisitString(StringExpression expression, SearchQueryInterpreter.Context context) => _inner.VisitString(expression, context);

    public SearchPredicate VisitChained(ChainedExpression expression, SearchQueryInterpreter.Context context) => _inner.VisitChained(expression, context);

    public SearchPredicate VisitMissingField(MissingFieldExpression expression, SearchQueryInterpreter.Context context) => _inner.VisitMissingField(expression, context);

    public SearchPredicate VisitMissingSearchParameter(MissingSearchParameterExpression expression, SearchQueryInterpreter.Context context) => _inner.VisitMissingSearchParameter(expression, context);

    public SearchPredicate VisitCompartment(CompartmentSearchExpression expression, SearchQueryInterpreter.Context context) => _inner.VisitCompartment(expression, context);

    public SearchPredicate VisitInclude(IncludeExpression expression, SearchQueryInterpreter.Context context) => _inner.VisitInclude(expression, context);

    public SearchPredicate VisitSortParameter(SortExpression expression, SearchQueryInterpreter.Context context) => _inner.VisitSortParameter(expression, context);

    public SearchPredicate VisitIn<T>(InExpression<T> expression, SearchQueryInterpreter.Context context) => _inner.VisitIn(expression, context);

    public SearchPredicate VisitPatientEverything(PatientEverythingExpression expression, SearchQueryInterpreter.Context context) => _inner.VisitPatientEverything(expression, context);

    public SearchPredicate VisitNotReferenced(NotReferencedExpression expression, SearchQueryInterpreter.Context context) => _inner.VisitNotReferenced(expression, context);

    public SearchPredicate VisitCompositeComponent(CompositeComponentExpression expression, SearchQueryInterpreter.Context context) => _inner.VisitCompositeComponent(expression, context);
}
