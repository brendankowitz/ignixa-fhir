// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search.Expressions;

public class CompositeComponentExpressionTests
{
    private static readonly SearchParameterInfo TokenComponentParam =
        new("code", "code", SearchParamType.Token);

    [Fact]
    public void GivenComponent_WhenConstructed_ThenExposesComponentSearchParameterPositionAndWrapped()
    {
        var wrapped = Expression.Equals(FieldName.TokenCode, 0, "8480-6");

        var component = new CompositeComponentExpression(TokenComponentParam, 0, wrapped);

        component.ComponentSearchParameter.ShouldBe(TokenComponentParam);
        component.Position.ShouldBe(0);
        component.WrappedExpression.ShouldBe(wrapped);
    }

    [Fact]
    public void GivenComponent_WhenAcceptVisitor_ThenDispatchesToVisitCompositeComponent()
    {
        var wrapped = Expression.Equals(FieldName.TokenCode, 0, "8480-6");
        var component = new CompositeComponentExpression(TokenComponentParam, 0, wrapped);
        var visitor = new RecordingVisitor();

        var result = component.AcceptVisitor(visitor, context: 0);

        result.ShouldBeSameAs(component);
        visitor.VisitedComponent.ShouldBeSameAs(component);
    }

    [Fact]
    public void GivenComponent_WhenToString_ThenIncludesPositionAndCode()
    {
        var wrapped = Expression.Equals(FieldName.TokenCode, 0, "8480-6");
        var component = new CompositeComponentExpression(TokenComponentParam, 1, wrapped);

        component.ToString().ShouldContain("[1]");
        component.ToString().ShouldContain("code");
    }

    [Fact]
    public void GivenTwoComponentsWithSamePositionAndEquivalentWrapped_WhenValueInsensitiveEquals_ThenTrue()
    {
        var a = new CompositeComponentExpression(TokenComponentParam, 0, Expression.Equals(FieldName.TokenCode, 0, "a"));
        var b = new CompositeComponentExpression(TokenComponentParam, 0, Expression.Equals(FieldName.TokenCode, 0, "b"));

        a.ValueInsensitiveEquals(b).ShouldBeTrue();
    }

    [Fact]
    public void GivenTwoComponentsWithDifferentPosition_WhenValueInsensitiveEquals_ThenFalse()
    {
        var a = new CompositeComponentExpression(TokenComponentParam, 0, Expression.Equals(FieldName.TokenCode, 0, "a"));
        var b = new CompositeComponentExpression(TokenComponentParam, 1, Expression.Equals(FieldName.TokenCode, 0, "a"));

        a.ValueInsensitiveEquals(b).ShouldBeFalse();
    }

    private sealed class RecordingVisitor : IExpressionVisitor<int, Expression>
    {
        public CompositeComponentExpression VisitedComponent { get; private set; }

        public Expression VisitCompositeComponent(CompositeComponentExpression expression, int context)
        {
            VisitedComponent = expression;
            return expression;
        }

        public Expression VisitSearchParameter(SearchParameterExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitBinary(BinaryExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitChained(ChainedExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitMissingField(MissingFieldExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitNotExpression(NotExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitMultiary(MultiaryExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitString(StringExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitCompartment(CompartmentSearchExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitInclude(IncludeExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitSortParameter(SortExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitIn<T>(InExpression<T> expression, int context) => throw new NotImplementedException();
        public Expression VisitUnion(UnionExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitPatientEverything(PatientEverythingExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitNotReferenced(NotReferencedExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitSearchParameterPredicate(SearchParameterPredicateExpression expression, int context) => throw new NotImplementedException();
    }
}
