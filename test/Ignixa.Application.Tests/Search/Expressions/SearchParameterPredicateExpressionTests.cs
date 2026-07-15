// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions;

public class SearchParameterPredicateExpressionTests
{
    [Fact]
    public void GivenAPredicateExpression_WhenAccepted_ThenDispatchesToVisitSearchParameterPredicate()
    {
        // Arrange
        var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var value = new StringSearchValue("example");
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, value);
        var visitor = new RecordingVisitor();

        // Act
        var result = predicate.AcceptVisitor(visitor, context: null);

        // Assert
        result.ShouldBe("visited-predicate");
        visitor.LastVisited.ShouldBeSameAs(predicate);
    }

    [Fact]
    public void GivenAPredicateExpression_WhenConstructed_ThenExposesParameterComparatorModifierAndValue()
    {
        // Arrange
        var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var value = new StringSearchValue("example");
        var modifier = new SearchModifier(SearchModifierCode.Exact);

        // Act
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ge, modifier, value);

        // Assert
        predicate.Parameter.ShouldBeSameAs(parameter);
        predicate.Comparator.ShouldBe(SearchComparator.Ge);
        predicate.Modifier.ShouldBe(modifier);
        predicate.Value.ShouldBeSameAs(value);
    }

    [Fact]
    public void GivenNullParameter_WhenConstructed_ThenThrows()
    {
        // Arrange
        var value = new StringSearchValue("example");

        // Act / Assert
        Should.Throw<ArgumentNullException>(() =>
            new SearchParameterPredicateExpression(null!, SearchComparator.Eq, modifier: null, value));
    }

    [Fact]
    public void GivenNullValue_WhenConstructed_ThenThrows()
    {
        // Arrange
        var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

        // Act / Assert
        Should.Throw<ArgumentNullException>(() =>
            new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, null!));
    }

    [Fact]
    public void GivenTwoPredicatesWithSameParameterComparatorAndModifier_WhenValueInsensitiveEquals_ThenTrue()
    {
        // Arrange
        var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var a = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("a"));
        var b = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("b"));

        // Act / Assert
        a.ValueInsensitiveEquals(b).ShouldBeTrue();
    }

    [Fact]
    public void GivenTwoPredicatesWithDifferentComparator_WhenValueInsensitiveEquals_ThenFalse()
    {
        // Arrange
        var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var a = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("a"));
        var b = new SearchParameterPredicateExpression(parameter, SearchComparator.Ge, modifier: null, new StringSearchValue("a"));

        // Act / Assert
        a.ValueInsensitiveEquals(b).ShouldBeFalse();
    }

    private sealed class RecordingVisitor : IExpressionVisitor<object?, string>
    {
        public SearchParameterPredicateExpression? LastVisited { get; private set; }

        public string VisitSearchParameterPredicate(SearchParameterPredicateExpression expression, object? context)
        {
            LastVisited = expression;
            return "visited-predicate";
        }

        public string VisitSearchParameter(SearchParameterExpression expression, object? context) => throw new NotImplementedException();
        public string VisitBinary(BinaryExpression expression, object? context) => throw new NotImplementedException();
        public string VisitChained(ChainedExpression expression, object? context) => throw new NotImplementedException();
        public string VisitMissingField(MissingFieldExpression expression, object? context) => throw new NotImplementedException();
        public string VisitMissingSearchParameter(MissingSearchParameterExpression expression, object? context) => throw new NotImplementedException();
        public string VisitNotExpression(NotExpression expression, object? context) => throw new NotImplementedException();
        public string VisitMultiary(MultiaryExpression expression, object? context) => throw new NotImplementedException();
        public string VisitString(StringExpression expression, object? context) => throw new NotImplementedException();
        public string VisitCompartment(CompartmentSearchExpression expression, object? context) => throw new NotImplementedException();
        public string VisitInclude(IncludeExpression expression, object? context) => throw new NotImplementedException();
        public string VisitSortParameter(SortExpression expression, object? context) => throw new NotImplementedException();
        public string VisitIn<T>(InExpression<T> expression, object? context) => throw new NotImplementedException();
        public string VisitUnion(UnionExpression expression, object? context) => throw new NotImplementedException();
        public string VisitPatientEverything(PatientEverythingExpression expression, object? context) => throw new NotImplementedException();
        public string VisitNotReferenced(NotReferencedExpression expression, object? context) => throw new NotImplementedException();
        public string VisitCompositeComponent(CompositeComponentExpression expression, object? context) => throw new NotImplementedException();
    }
}
