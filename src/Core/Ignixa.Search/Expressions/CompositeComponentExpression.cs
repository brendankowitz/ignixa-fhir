// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Wraps one component of a composite search parameter expression, carrying the component's
/// effective (value-inferred) <see cref="SearchParameterInfo"/> and position from parse time
/// through to query generation. Does not implement <see cref="IFieldExpression"/> - the wrapped
/// expression is frequently a <see cref="MultiaryExpression"/> with no single field name, and
/// nothing needs to query this type's identity through that interface.
/// </summary>
public sealed class CompositeComponentExpression : Expression
{
    public CompositeComponentExpression(SearchParameterInfo componentSearchParameter, int position, Expression wrappedExpression)
    {
        EnsureArg.IsNotNull(componentSearchParameter, nameof(componentSearchParameter));
        EnsureArg.IsNotNull(wrappedExpression, nameof(wrappedExpression));

        ComponentSearchParameter = componentSearchParameter;
        Position = position;
        WrappedExpression = wrappedExpression;
    }

    /// <summary>
    /// Gets the effective search parameter for this component - the value-inferred type when it
    /// diverges from the static component definition (e.g. DocumentReference's swapped
    /// <c>relationship</c> component definitions), otherwise the static definition itself.
    /// </summary>
    public SearchParameterInfo ComponentSearchParameter { get; }

    /// <summary>
    /// Gets the zero-based position of this component within the composite search parameter.
    /// </summary>
    public int Position { get; }

    /// <summary>
    /// Gets the expression built for this component's value.
    /// </summary>
    public Expression WrappedExpression { get; }

    public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
    {
        EnsureArg.IsNotNull(visitor, nameof(visitor));

        return visitor.VisitCompositeComponent(this, context);
    }

    public override string ToString()
        => $"(Component[{Position}] {ComponentSearchParameter.Code} {WrappedExpression})";

    public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
    {
        hashCode.Add(typeof(CompositeComponentExpression));
        hashCode.Add(Position);
        WrappedExpression.AddValueInsensitiveHashCode(ref hashCode);
    }

    public override bool ValueInsensitiveEquals(Expression other)
        => other is CompositeComponentExpression cce &&
           cce.Position == Position &&
           WrappedExpression.ValueInsensitiveEquals(cce.WrappedExpression);
}
