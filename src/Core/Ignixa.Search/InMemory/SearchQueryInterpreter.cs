// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using EnsureThat;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;

namespace Ignixa.Search.InMemory;

/// <summary>
/// Expression visitor that converts FHIR search parameter expressions into predicates for in-memory filtering.
/// Ported from microsoft/fhir-server feature/subscription-engine branch.
/// </summary>
/// <remarks>
/// <para>
/// The expression beneath each <see cref="SearchParameterExpression"/> is lowered to the field-level shape and
/// compiled into a test of one index entry (see <see cref="SearchIndexEntryMatcher"/>): a resource matches when
/// one of its entries for the parameter satisfies the whole criterion. A token <c>:not</c> matches resources with
/// no entry satisfying the criterion, including resources without the parameter.
/// </para>
/// <para>
/// <c>_id</c> and <c>_type</c> are read from <see cref="Abstractions.ResourceKey"/>, since they never have index
/// entries.
/// </para>
/// </remarks>
[CLSCompliant(false)]
public sealed class SearchQueryInterpreter : IExpressionVisitorWithInitialContext<SearchQueryInterpreter.Context, SearchPredicate>
{
    /// <summary>
    /// Gets the initial context for expression visiting.
    /// </summary>
    public Context InitialContext => default;

    public SearchPredicate VisitSearchParameter(SearchParameterExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        string parameterName = expression.Parameter.Name;
        Expression body = LegacyExpressionLowerer.LowerToLegacy(expression.Expression);

        if (expression.Parameter.Code == SearchParameterNames.ResourceType)
        {
            return input => input.Where(x => MatchesKeyField(body, x.Location.ResourceType, SearchParameterNames.ResourceType));
        }

        if (expression.Parameter.Code == SearchParameterNames.Id)
        {
            return input => input.Where(x => MatchesKeyField(body, x.Location.Id, SearchParameterNames.Id));
        }

        if (body is NotExpression not)
        {
            Func<SearchIndexEntry, bool> negated = SearchIndexEntryMatcher.Compile(not.Expression);
            return input => input.Where(x => !x.Index.Any(y => y.SearchParameter.Name == parameterName && negated(y)));
        }

        Func<SearchIndexEntry, bool> matcher = SearchIndexEntryMatcher.Compile(body);
        return input => input.Where(x => x.Index.Any(y => y.SearchParameter.Name == parameterName && matcher(y)));
    }

    public SearchPredicate VisitSearchParameterPredicate(SearchParameterPredicateExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        return VisitSearchParameter(Expression.SearchParameter(expression.Parameter, expression), context);
    }

    public SearchPredicate VisitCompositeComponent(CompositeComponentExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        return VisitFieldLevel(expression, context);
    }

    // Binary, String and MissingField nodes normally sit beneath a SearchParameterExpression, which compiles them
    // per entry. Reached directly, they are tested against the entries of the parameter named by the context.
    public SearchPredicate VisitBinary(BinaryExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        return VisitFieldLevel(expression, context);
    }

    public SearchPredicate VisitString(StringExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        if (context.ParameterName == SearchParameterNames.ResourceType)
        {
            return input => input.Where(x => MatchesKeyField(expression, x.Location.ResourceType, SearchParameterNames.ResourceType));
        }

        return VisitFieldLevel(expression, context);
    }

    public SearchPredicate VisitMissingField(MissingFieldExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        return VisitFieldLevel(expression, context);
    }

    public SearchPredicate VisitMissingSearchParameter(MissingSearchParameterExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        // Every resource has an id and none has an _id index entry.
        if (expression.Parameter.Code == SearchParameterNames.Id)
        {
            return expression.IsMissing
                ? input => Enumerable.Empty<(Abstractions.ResourceKey, IReadOnlyCollection<SearchIndexEntry>)>()
                : input => input;
        }

        string parameterName = expression.Parameter.Name;
        return expression.IsMissing
            ? input => input.Where(x => !x.Index.Any(y => y.SearchParameter.Name == parameterName))
            : input => input.Where(x => x.Index.Any(y => y.SearchParameter.Name == parameterName));
    }

    public SearchPredicate VisitMultiary(MultiaryExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull(expression.Expressions, nameof(expression.Expressions));

        return expression.Expressions
            .Select(x => x.AcceptVisitor(this, context))
            .Aggregate((x, y) => expression.MultiaryOperation switch
            {
                MultiaryOperator.And => p => x(p).Intersect(y(p)),
                MultiaryOperator.Or => p => x(p).Union(y(p)),
                _ => throw new SearchOperationNotSupportedException($"MultiaryOperator {expression.MultiaryOperation} is not supported in in-memory search."),
            });
    }

    public SearchPredicate VisitUnion(UnionExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        return expression.Expressions
            .Select(x => x.AcceptVisitor(this, context))
            .Aggregate((x, y) => p => x(p).Union(y(p)));
    }

    public SearchPredicate VisitNotExpression(NotExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        SearchPredicate inner = expression.Expression.AcceptVisitor(this, context);
        return input => input.Except(inner(input));
    }

    public SearchPredicate VisitChained(ChainedExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        throw new SearchOperationNotSupportedException("ChainedExpression is not supported.");
    }

    public SearchPredicate VisitCompartment(CompartmentSearchExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        throw new SearchOperationNotSupportedException("Compartment search is not supported.");
    }

    public SearchPredicate VisitPatientEverything(PatientEverythingExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        throw new SearchOperationNotSupportedException("Patient $everything is not supported in in-memory search.");
    }

    public SearchPredicate VisitInclude(IncludeExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        throw new SearchOperationNotSupportedException("IncludeExpression is not supported in in-memory search.");
    }

    public SearchPredicate VisitSortParameter(SortExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        throw new SearchOperationNotSupportedException("SortExpression is not supported in in-memory search.");
    }

    public SearchPredicate VisitIn<T>(InExpression<T> expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        throw new SearchOperationNotSupportedException("InExpression is not supported in in-memory search.");
    }

    public SearchPredicate VisitNotReferenced(NotReferencedExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        throw new SearchOperationNotSupportedException("_not-referenced is not supported in in-memory search.");
    }

    private static SearchPredicate VisitFieldLevel(Expression expression, Context context)
    {
        string? parameterName = context.ParameterName;
        Func<SearchIndexEntry, bool> matcher = SearchIndexEntryMatcher.Compile(LegacyExpressionLowerer.LowerToLegacy(expression));
        return input => input.Where(x => x.Index.Any(y => y.SearchParameter.Name == parameterName && matcher(y)));
    }

    /// <summary>Evaluates the lowered body of <c>_id</c> or <c>_type</c> against the resource key field it searches.</summary>
    private static bool MatchesKeyField(Expression expression, string value, string parameterCode) =>
        expression switch
        {
            StringExpression str => str.StringOperator switch
            {
                StringOperator.Equals => string.Equals(value, str.Value, str.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal),
                _ => throw new SearchOperationNotSupportedException($"StringOperator {str.StringOperator} is not supported for {parameterCode} in in-memory search."),
            },

            // Each resource has exactly one id and one type, so negating the comparison is "no entry matches".
            NotExpression not => !MatchesKeyField(not.Expression, value, parameterCode),
            MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } multiary => multiary.Expressions.Any(x => MatchesKeyField(x, value, parameterCode)),
            MultiaryExpression { MultiaryOperation: MultiaryOperator.And } multiary => multiary.Expressions.All(x => MatchesKeyField(x, value, parameterCode)),
            UnionExpression union => union.Expressions.Any(x => MatchesKeyField(x, value, parameterCode)),
            _ => throw new SearchOperationNotSupportedException($"{expression.GetType().Name} is not supported for {parameterCode} in in-memory search."),
        };

    /// <summary>
    /// Context that is passed through the visit.
    /// </summary>
    public struct Context : IEquatable<Context>
    {
        public string? ParameterName { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Public API")]
        public Context WithParameterName(string paramName)
        {
            return new Context
            {
                ParameterName = paramName,
            };
        }

        public readonly bool Equals(Context other)
        {
            return ParameterName == other.ParameterName;
        }

        public override readonly bool Equals(object? obj)
        {
            return obj is Context context && Equals(context);
        }

        public override readonly int GetHashCode()
        {
            return ParameterName?.GetHashCode(StringComparison.Ordinal) ?? 0;
        }

        public static bool operator ==(Context left, Context right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Context left, Context right)
        {
            return !(left == right);
        }
    }
}
