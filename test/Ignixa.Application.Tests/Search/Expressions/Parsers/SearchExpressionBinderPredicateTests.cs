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

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

/// <summary>
/// Characterization tests pinning the exact <see cref="SearchParameterPredicateExpression"/>/
/// <see cref="CompositeComponentExpression"/> tree shape <see cref="SearchExpressionBinder"/> now
/// builds as its canonical output (see phase2 plan task 4,
/// docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-phase2-semantic-ir.md). Mirrors
/// <see cref="ExpressionParserCharacterizationTests"/>'s pattern.
/// </summary>
public class SearchExpressionBinderPredicateTests
{
    private static readonly string[] PatientResourceType = ["Patient"];
    private static readonly string[] ObservationResourceType = ["Observation"];

    [Fact]
    public void GivenPatientStringSearch_WhenParsingNameEqualsSmith_ThenReturnsSearchParameterPredicateExpression()
    {
        var context = new SearchParserTestContext();
        var searchParameter = context.Add("Patient", "name", SearchParamType.String);

        var expression = context.Parser.Parse(PatientResourceType, "name", "Smith");

        var searchParameterExpression = expression.ShouldBeOfType<SearchParameterExpression>();
        var predicate = searchParameterExpression.Expression.ShouldBeOfType<SearchParameterPredicateExpression>();
        predicate.Parameter.ShouldBeSameAs(searchParameter);
        predicate.Comparator.ShouldBe(SearchComparator.Eq);
        predicate.Modifier.ShouldBeNull();
        var value = predicate.Value.ShouldBeOfType<StringSearchValue>();
        value.String.ShouldBe("Smith");
    }

    [Fact]
    public void GivenPatientStringSearch_WhenParsingNameExactModifierEqualsSmith_ThenPredicateCarriesExactModifier()
    {
        var context = new SearchParserTestContext();
        var searchParameter = context.Add("Patient", "name", SearchParamType.String);

        var expression = context.Parser.Parse(PatientResourceType, "name:exact", "Smith");

        var searchParameterExpression = expression.ShouldBeOfType<SearchParameterExpression>();
        var predicate = searchParameterExpression.Expression.ShouldBeOfType<SearchParameterPredicateExpression>();
        predicate.Parameter.ShouldBeSameAs(searchParameter);
        predicate.Comparator.ShouldBe(SearchComparator.Eq);
        predicate.Modifier.ShouldBe(new SearchModifier(SearchModifierCode.Exact));
        var value = predicate.Value.ShouldBeOfType<StringSearchValue>();
        value.String.ShouldBe("Smith");
    }

    [Fact]
    public void GivenObservationCompositeCodeValueQuantitySearch_WhenParsing_ThenBuildsAndOfCompositeComponentExpressions()
    {
        var context = new SearchParserTestContext();
        var codeParam = context.Add("Observation", "component-code", SearchParamType.Token);
        var quantityParam = context.Add("Observation", "component-value-quantity", SearchParamType.Quantity);
        var composite = context.Add(
            "Observation",
            "component-code-value-quantity",
            SearchParamType.Composite,
            components:
            [
                new SearchParameterComponentInfo { ResolvedSearchParameter = codeParam },
                new SearchParameterComponentInfo { ResolvedSearchParameter = quantityParam },
            ]);

        var expression = context.Parser.Parse(
            ObservationResourceType,
            "component-code-value-quantity",
            "http://loinc.org|8480-6$107");

        var searchParameterExpression = expression.ShouldBeOfType<SearchParameterExpression>();
        searchParameterExpression.Parameter.ShouldBeSameAs(composite);

        var and = searchParameterExpression.Expression.ShouldBeOfType<MultiaryExpression>();
        and.MultiaryOperation.ShouldBe(MultiaryOperator.And);
        and.Expressions.Count.ShouldBe(2);

        var codeComponent = and.Expressions[0].ShouldBeOfType<CompositeComponentExpression>();
        codeComponent.Position.ShouldBe(0);
        codeComponent.ComponentSearchParameter.ShouldBeSameAs(codeParam);
        var codePredicate = codeComponent.WrappedExpression.ShouldBeOfType<SearchParameterPredicateExpression>();
        codePredicate.Comparator.ShouldBe(SearchComparator.Eq);
        codePredicate.Modifier.ShouldBeNull();
        var codeValue = codePredicate.Value.ShouldBeOfType<TokenSearchValue>();
        codeValue.System.ShouldBe("http://loinc.org");
        codeValue.Code.ShouldBe("8480-6");

        var quantityComponent = and.Expressions[1].ShouldBeOfType<CompositeComponentExpression>();
        quantityComponent.Position.ShouldBe(1);
        quantityComponent.ComponentSearchParameter.ShouldBeSameAs(quantityParam);
        var quantityPredicate = quantityComponent.WrappedExpression.ShouldBeOfType<SearchParameterPredicateExpression>();
        quantityPredicate.Comparator.ShouldBe(SearchComparator.Eq);
        quantityPredicate.Modifier.ShouldBeNull();
        var quantityValue = quantityPredicate.Value.ShouldBeOfType<QuantitySearchValue>();
        quantityValue.Low.ShouldBe(107m);
        quantityValue.High.ShouldBe(107m);
    }
}
