// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Sparky.Extensions.ValueSets;
using Sparky.Search.Expressions;
using Sparky.Search.Indexing;
using Sparky.Search.Indexing.SearchValues;

namespace Sparky.Search.InMemory;

/// <summary>
/// Expression visitor that converts FHIR search parameter expressions into predicates for in-memory filtering.
/// Ported from microsoft/fhir-server feature/subscription-engine branch.
/// </summary>
internal class SearchQueryInterpreter : IExpressionVisitorWithInitialContext<SearchQueryInterpreter.Context, SearchPredicate>
{
    Context IExpressionVisitorWithInitialContext<Context, SearchPredicate>.InitialContext => default;

    public SearchPredicate VisitSearchParameter(SearchParameterExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull<Context>(context, nameof(context));

        return VisitInnerWithContext(expression.Parameter.Name, expression.Expression, context);
    }

    public SearchPredicate VisitBinary(BinaryExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull<Context>(context, nameof(context));

        return VisitBinary(context.ParameterName, expression.BinaryOperator, expression.Value);
    }

    private static SearchPredicate VisitBinary(string fieldName, BinaryOperator op, object value)
    {
        EnsureArg.IsNotNull(fieldName, nameof(fieldName));
        EnsureArg.IsNotNull(value, nameof(value));

        return input => input.Where(x => x.Index.Any(y => y.SearchParameter.Name == fieldName && GetMappedValue(op, y.Value, (IComparable)value)));
    }

    private static bool GetMappedValue(BinaryOperator expressionBinaryOperator, ISearchValue first, IComparable second)
    {
        EnsureArg.IsNotNull(first, nameof(first));
        EnsureArg.IsNotNull(second, nameof(second));

        if (first == null || second == null)
        {
            return false;
        }

        var comparisonVisitor = new ComparisonValueVisitor(expressionBinaryOperator, second);
        first.AcceptVisitor(comparisonVisitor);

        return comparisonVisitor.Compare();
    }

    public SearchPredicate VisitChained(ChainedExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull<Context>(context, nameof(context));

        throw new SearchOperationNotSupportedException("ChainedExpression is not supported.");
    }

    public SearchPredicate VisitMissingField(MissingFieldExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull<Context>(context, nameof(context));

        throw new NotImplementedException();
    }

    public SearchPredicate VisitMissingSearchParameter(MissingSearchParameterExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull<Context>(context, nameof(context));

        throw new NotImplementedException();
    }

    public SearchPredicate VisitMultiary(MultiaryExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull(expression.Expressions, nameof(expression.Expressions));
        EnsureArg.IsNotNull<Context>(context, nameof(context));

        return expression.Expressions
            .Select(x => x.AcceptVisitor(this, context))
            .Aggregate((x, y) =>
            {
                return expression.MultiaryOperation switch
                {
                    MultiaryOperator.And => p => x(p).Intersect(y(p)),
                    MultiaryOperator.Or => p => x(p).Union(y(p)),
                    _ => throw new NotImplementedException($"MultiaryOperator {expression.MultiaryOperation} not supported")
                };
            });
    }

    public SearchPredicate VisitString(StringExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull<Context>(context, nameof(context));

        StringComparison comparison = expression.IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (context.ParameterName == "_type")
        {
            return input => input.Where(x => x.Location.ResourceType.Equals(expression.Value, comparison));
        }
        else
        {
            return expression.StringOperator switch
            {
                StringOperator.StartsWith => input => input.Where(x => x.Index.Any(y =>
                    y.SearchParameter.Name == context.ParameterName &&
                    CompareStringParameter(y, (a, b, c) => a.StartsWith(b, c)))),

                StringOperator.Equals => input => input.Where(x => x.Index.Any(y =>
                    y.SearchParameter.Name == context.ParameterName &&
                    CompareStringParameter(y, string.Equals))),

                _ => throw new NotImplementedException($"StringOperator {expression.StringOperator} not supported")
            };
        }

        bool CompareStringParameter(SearchIndexEntry y, Func<string, string, StringComparison, bool> compareFunc)
        {
            EnsureArg.IsNotNull(y, nameof(y));

            return y.SearchParameter.Type switch
            {
                SearchParamType.String => compareFunc(((StringSearchValue)y.Value).String, expression.Value, comparison),

                SearchParamType.Token => compareFunc(((TokenSearchValue)y.Value).Code, expression.Value, comparison) ||
                                        compareFunc(((TokenSearchValue)y.Value).System, expression.Value, comparison),

                _ => throw new NotImplementedException($"Search parameter type {y.SearchParameter.Type} not supported for string comparison")
            };
        }
    }

    public SearchPredicate VisitCompartment(CompartmentSearchExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull<Context>(context, nameof(context));

        throw new SearchOperationNotSupportedException("Compartment search is not supported.");
    }

    public SearchPredicate VisitInclude(IncludeExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull<Context>(context, nameof(context));

        throw new NotImplementedException();
    }

    private SearchPredicate VisitInnerWithContext(string parameterName, Expression expression, Context context, bool negate = false)
    {
        EnsureArg.IsNotNull(parameterName, nameof(parameterName));
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull<Context>(context, nameof(context));

        var newContext = context.WithParameterName(parameterName);

        SearchPredicate filter = input =>
        {
            if (expression != null)
            {
                return expression.AcceptVisitor(this, newContext)(input);
            }
            else
            {
                // :missing will end up here
                throw new NotSupportedException("This query is not supported");
            }
        };

        if (negate)
        {
            SearchPredicate inner = filter;
            filter = input => input.Except(inner(input));
        }

        return filter;
    }

    public SearchPredicate VisitNotExpression(NotExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull<Context>(context, nameof(context));

        throw new NotImplementedException();
    }

    public SearchPredicate VisitSortParameter(SortExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull<Context>(context, nameof(context));

        throw new NotImplementedException();
    }

    /// <summary>
    /// Context that is passed through the visit.
    /// </summary>
    internal struct Context
    {
        public string ParameterName { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Internal API")]
        public Context WithParameterName(string paramName)
        {
            return new Context
            {
                ParameterName = paramName,
            };
        }
    }
}
