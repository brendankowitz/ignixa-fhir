// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using EnsureThat;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Represents a semantic/vector search over a <c>special</c> search parameter carrying a
/// <see cref="VectorSearchConfig"/> (<see cref="SearchParameterInfo.IsSemantic"/>). Carries the raw,
/// verbatim query text from parse time (<see cref="QueryText"/>) through to query generation, where it
/// is embedded exactly once into <see cref="Prepared"/> — critically, <em>not</em> split on commas or
/// otherwise decomposed the way other search values are, since the text is one query, not a set of
/// alternatives (see Review Focus #1 in the slice-1 plan).
/// </summary>
public sealed class VectorSearchExpression : Expression
{
    public VectorSearchExpression(SearchParameterInfo parameter, string queryText)
    {
        EnsureArg.IsNotNull(parameter, nameof(parameter));
        EnsureArg.IsNotNullOrWhiteSpace(queryText, nameof(queryText));

        Parameter = parameter;
        QueryText = queryText;
    }

    /// <summary>Gets the semantic search parameter this expression is bound to.</summary>
    public SearchParameterInfo Parameter { get; }

    /// <summary>Gets the verbatim query text, exactly as supplied by the caller (trimmed only at the ends).</summary>
    public string QueryText { get; }

    /// <summary>
    /// Gets the embedded form of <see cref="QueryText"/>, or null before query-time embedding has run.
    /// Set via <see cref="WithPrepared"/>; populating and consuming this is Task 7's concern, not this
    /// type's — it only carries the value through the (otherwise immutable) expression tree.
    /// </summary>
    public PreparedVectorQuery? Prepared { get; private init; }

    /// <summary>
    /// Returns a new instance carrying <paramref name="prepared"/>. Expression trees are treated as
    /// immutable elsewhere in this namespace (see <see cref="ExpressionRewriter{TContext}"/>'s remarks),
    /// so this returns a copy rather than mutating this instance in place.
    /// </summary>
    public VectorSearchExpression WithPrepared(PreparedVectorQuery prepared)
    {
        EnsureArg.IsNotNull(prepared, nameof(prepared));

        return new VectorSearchExpression(Parameter, QueryText) { Prepared = prepared };
    }

    public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
    {
        EnsureArg.IsNotNull(visitor, nameof(visitor));

        return visitor.VisitVectorSearch(this, context);
    }

    public override string ToString()
        => $"(VectorSearch {Parameter.Code} \"{QueryText}\")";

    // QueryText is the parameterizable value here (like StringExpression.Value) and is deliberately
    // excluded, matching Expression.ValueInsensitiveEquals's documented contract.
    public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
    {
        hashCode.Add(typeof(VectorSearchExpression));
        hashCode.Add(Parameter);
    }

    public override bool ValueInsensitiveEquals(Expression other)
        => other is VectorSearchExpression vse && vse.Parameter.Equals(Parameter);
}
