// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using EnsureThat;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

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
/// entries. Chained and reverse-chained (<c>_has</c>) parameters are evaluated only when the interpreter was
/// created with an <see cref="IInMemorySearchSource"/> and the matching <see cref="InMemorySearchFeatures"/>;
/// otherwise they throw <see cref="SearchOperationNotSupportedException"/>.
/// </para>
/// </remarks>
[CLSCompliant(false)]
public sealed class SearchQueryInterpreter : IExpressionVisitorWithInitialContext<SearchQueryInterpreter.Context, SearchPredicate>
{
    private readonly IInMemorySearchSource? _source;
    private readonly InMemorySearchFeatures _features;

    /// <summary>Creates an interpreter that filters one resource type; chains and <c>_has</c> are not supported.</summary>
    public SearchQueryInterpreter()
    {
    }

    /// <summary>
    /// Creates an interpreter that can follow references into <paramref name="source"/> for the enabled
    /// <paramref name="features"/>. Experimental (<c>IGNIXAEXP001</c>).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental(InMemoryExperimental.DiagnosticId)]
    public SearchQueryInterpreter(IInMemorySearchSource source, InMemorySearchFeatures features)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _features = features;
    }

    /// <summary>
    /// Gets the initial context for expression visiting.
    /// </summary>
    public Context InitialContext => default;

    public SearchPredicate VisitSearchParameter(SearchParameterExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        string parameterName = expression.Parameter.Name;
        if (expression.Parameter.ComponentInconsistency is { } inconsistency)
        {
            throw new SearchOperationNotSupportedException(
                $"Composite search parameter '{expression.Parameter.Code}' cannot be searched in memory: {inconsistency}.");
        }

        Expression body = LegacyExpressionLowerer.LowerToLegacy(expression.Expression);

        if (expression.Parameter.Code == SearchParameterNames.ResourceType)
        {
            Func<string, bool> matchesType = CompileKeyField(body, SearchParameterNames.ResourceType);
            return input => input.Where(x => matchesType(x.Location.ResourceType));
        }

        if (expression.Parameter.Code == SearchParameterNames.Id)
        {
            Func<string, bool> matchesId = CompileKeyField(body, SearchParameterNames.Id);
            return input => input.Where(x => matchesId(x.Location.Id));
        }

        if (body is NotExpression not)
        {
            Func<SearchIndexEntry, bool> negated = WithCanonicals(SearchIndexEntryMatcher.Compile(not.Expression, expression.Parameter), expression, negatedBody: true);
            return input => input.Where(x => !x.Index.Any(y => y.SearchParameter.Name == parameterName && negated(y)));
        }

        Func<SearchIndexEntry, bool> matcher = WithCanonicals(SearchIndexEntryMatcher.Compile(body, expression.Parameter), expression, negatedBody: false);
        return input => input.Where(x => x.Index.Any(y => y.SearchParameter.Name == parameterName && matcher(y)));
    }

    /// <summary>
    /// For a reference parameter that selects canonicals, extends <paramref name="matcher"/> to the canonical entries
    /// <see cref="CanonicalReferenceSearchIndexer"/> adds, which the field-level matcher never matches.
    /// </summary>
    private static Func<SearchIndexEntry, bool> WithCanonicals(Func<SearchIndexEntry, bool> matcher, SearchParameterExpression expression, bool negatedBody)
    {
        if (expression.Parameter.CanonicalReferences == CanonicalReferenceKind.None)
        {
            return matcher;
        }

        Expression positive = negatedBody && expression.Expression is NotExpression { Expression: { } inner } ? inner : expression.Expression;
        Func<UriSearchValue, bool> canonical = CanonicalReferenceMatcher.Compile(positive);
        return entry => entry.Value is UriSearchValue uri ? canonical(uri) : matcher(entry);
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
            Func<string, bool> matchesType = CompileKeyField(expression, SearchParameterNames.ResourceType);
            return input => input.Where(x => matchesType(x.Location.ResourceType));
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
                MultiaryOperator.And => p => x(p).Intersect(y(p), ByLocation.Instance),
                MultiaryOperator.Or => p => x(p).Union(y(p), ByLocation.Instance),
                _ => throw new SearchOperationNotSupportedException($"MultiaryOperator {expression.MultiaryOperation} is not supported in in-memory search."),
            });
    }

    public SearchPredicate VisitUnion(UnionExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        return expression.Expressions
            .Select(x => x.AcceptVisitor(this, context))
            .Aggregate((x, y) => p => x(p).Union(y(p), ByLocation.Instance));
    }

    public SearchPredicate VisitNotExpression(NotExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        SearchPredicate inner = expression.Expression.AcceptVisitor(this, context);
        return input => input.Except(inner(input), ByLocation.Instance);
    }

    public SearchPredicate VisitChained(ChainedExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        InMemorySearchFeatures required = expression.Reversed ? InMemorySearchFeatures.ReverseChains : InMemorySearchFeatures.Chains;
        if (_source is null || !_features.HasFlag(required))
        {
            throw new SearchOperationNotSupportedException(expression.Reversed
                ? "Reverse chained (_has) search is not enabled for in-memory search."
                : "ChainedExpression is not supported.");
        }

        RefuseCanonicalOnly(expression.ReferenceSearchParameter, expression.Reversed ? "_has" : "a chain");
        SearchPredicate inner = expression.Expression.AcceptVisitor(this, InitialContext);
        return expression.Reversed
            ? ReverseChain(expression, inner, _source)
            : ForwardChain(expression, inner, _source);
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

    // _include and _sort are not filters; InMemorySearchEngine applies them from SearchOptions.
    public SearchPredicate VisitInclude(IncludeExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        throw new SearchOperationNotSupportedException("IncludeExpression is not a filter; use InMemorySearchEngine to resolve includes.");
    }

    public SearchPredicate VisitSortParameter(SortExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        throw new SearchOperationNotSupportedException("SortExpression is not a filter; use InMemorySearchEngine to sort.");
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
        Func<SearchIndexEntry, bool> matcher = SearchIndexEntryMatcher.Compile(LegacyExpressionLowerer.LowerToLegacy(expression), parameter: null);
        return input => input.Where(x => x.Index.Any(y => y.SearchParameter.Name == parameterName && matcher(y)));
    }

    /// <summary>
    /// A resource matches when one of its references through the chain's parameter points at a resource of a
    /// target type that matches the chain's inner expression. The targets are evaluated once per predicate call.
    /// </summary>
    private static SearchPredicate ForwardChain(ChainedExpression chain, SearchPredicate inner, IInMemorySearchSource source) =>
        input =>
        {
            HashSet<(string ResourceType, string Id)> targets = [.. chain.TargetResourceTypes
                .SelectMany(type => inner(source.Enumerate(type)))
                .Select(match => (match.Location.ResourceType, match.Location.Id))];

            return input.Where(x => LocalReferences(x.Index, chain.ReferenceSearchParameter).Any(targets.Contains));
        };

    /// <summary>
    /// A resource matches when a resource of the chain's source type both matches the chain's inner expression and
    /// references it through the chain's parameter.
    /// </summary>
    private static SearchPredicate ReverseChain(ChainedExpression chain, SearchPredicate inner, IInMemorySearchSource source) =>
        input =>
        {
            HashSet<(string ResourceType, string Id)> referenced = [.. chain.ResourceTypes
                .SelectMany(type => inner(source.Enumerate(type)))
                .SelectMany(match => LocalReferences(match.Index, chain.ReferenceSearchParameter))];

            return input.Where(x => referenced.Contains((x.Location.ResourceType, x.Location.Id)));
        };

    /// <summary>
    /// Refuses following <paramref name="parameter"/> when it selects only canonicals, decided from its definition so the
    /// answer never depends on the data or the order of its index entries. Resolving a canonical to the resource it names is
    /// a value join this engine does not perform, so following such a parameter would always find nothing.
    /// </summary>
    /// <exception cref="SearchOperationNotSupportedException">The parameter selects only canonicals.</exception>
    internal static void RefuseCanonicalOnly(SearchParameterInfo parameter, string follower)
    {
        if (parameter.CanonicalReferences == CanonicalReferenceKind.Only)
        {
            throw new SearchOperationNotSupportedException(
                $"Search parameter '{parameter.Code}' selects only canonical references; canonical resolution is not supported, so {follower} cannot follow it in in-memory search.");
        }
    }

    /// <summary>
    /// The resources <paramref name="index"/> references through <paramref name="parameter"/>, or through every reference
    /// parameter when it is null, by type and id. A reference with a base URI points at another server, and one without a
    /// type cannot be resolved, so neither names a resource the source could hold. A canonical entry
    /// (<see cref="CanonicalReferenceSearchIndexer"/>) is skipped: it is never followed, whatever entries surround it.
    /// </summary>
    internal static IEnumerable<(string ResourceType, string Id)> LocalReferences(IEnumerable<SearchIndexEntry> index, SearchParameterInfo? parameter) =>
        index
            .Where(entry => parameter is null ? entry.SearchParameter.Type == SearchParamType.Reference : entry.SearchParameter.Name == parameter.Name)
            .Select(entry => entry.Value)
            .OfType<ReferenceSearchValue>()
            .Where(reference => reference.BaseUri is null && !string.IsNullOrEmpty(reference.ResourceType) && !string.IsNullOrEmpty(reference.ResourceId))
            .Select(reference => (reference.ResourceType, reference.ResourceId));

    /// <summary>
    /// Compiles the lowered body of <c>_id</c> or <c>_type</c> into a test of the resource key field it searches. Each
    /// resource has exactly one id and one type, so negating the comparison is "no value matches".
    /// </summary>
    /// <exception cref="BadSearchRequestException">The value names a system, which an id or type never has.</exception>
    private static Func<string, bool> CompileKeyField(Expression expression, string parameterCode)
    {
        switch (expression)
        {
            case StringExpression { StringOperator: StringOperator.Equals, FieldName: not (FieldName.TokenSystem or FieldName.IdentifierTypeSystem or FieldName.IdentifierTypeCode) } str:
                StringComparison comparison = str.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return value => string.Equals(value, str.Value, comparison);
            case StringExpression { FieldName: FieldName.TokenSystem } or MissingFieldExpression { FieldName: FieldName.TokenSystem }:
                throw new BadSearchRequestException($"{parameterCode} values have no system; search {parameterCode}=value without a '|'.");
            case NotExpression not:
                Func<string, bool> negated = CompileKeyField(not.Expression, parameterCode);
                return value => !negated(value);
            case MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or UnionExpression:
                Func<string, bool>[] alternatives = [.. Children(expression).Select(child => CompileKeyField(child, parameterCode))];
                return value => alternatives.Any(alternative => alternative(value));
            case MultiaryExpression { MultiaryOperation: MultiaryOperator.And } multiary:
                Func<string, bool>[] all = [.. multiary.Expressions.Select(child => CompileKeyField(child, parameterCode))];
                return value => all.All(condition => condition(value));
            default:
                throw new SearchOperationNotSupportedException($"{expression} is not supported for {parameterCode} in in-memory search.");
        }
    }

    private static IReadOnlyList<Expression> Children(Expression expression) =>
        expression switch
        {
            MultiaryExpression multiary => multiary.Expressions,
            UnionExpression union => union.Expressions,
            _ => [expression],
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
