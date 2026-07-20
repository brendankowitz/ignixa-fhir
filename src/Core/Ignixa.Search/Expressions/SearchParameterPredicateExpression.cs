// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using EnsureThat;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions;

/// <summary>
/// A single typed predicate over one search parameter — the parameter's identity, how the value is
/// compared, and the value itself, typed as the same <see cref="ISearchValue"/> the parser already
/// builds during parsing rather than an untyped <see cref="object"/>. This is the parser's canonical
/// leaf; old-shape backends reach the flattened form via <see cref="LegacyExpressionLowerer"/>.
/// </summary>
public sealed class SearchParameterPredicateExpression : Expression
{
    public SearchParameterPredicateExpression(SearchParameterInfo parameter, SearchComparator comparator, SearchModifier? modifier, ISearchValue value)
    {
        EnsureArg.IsNotNull(parameter, nameof(parameter));
        EnsureArg.IsNotNull(value, nameof(value));

        Parameter = parameter;
        Comparator = comparator;
        Modifier = modifier;
        Value = value;
    }

    public SearchParameterInfo Parameter { get; }

    public SearchComparator Comparator { get; }

    public SearchModifier? Modifier { get; }

    public ISearchValue Value { get; }

    public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
    {
        EnsureArg.IsNotNull(visitor, nameof(visitor));

        return visitor.VisitSearchParameterPredicate(this, context);
    }

    public override string ToString()
        => $"(Predicate {Parameter.Code} {Comparator}{(Modifier == null ? null : $":{Modifier}")} {Value})";

    public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
    {
        hashCode.Add(typeof(SearchParameterPredicateExpression));
        hashCode.Add(Parameter);
        hashCode.Add(Comparator);
        hashCode.Add(Modifier);
    }

    public override bool ValueInsensitiveEquals(Expression other)
        => other is SearchParameterPredicateExpression p &&
           p.Parameter.Equals(Parameter) &&
           p.Comparator == Comparator &&
           p.Modifier == Modifier;
}
