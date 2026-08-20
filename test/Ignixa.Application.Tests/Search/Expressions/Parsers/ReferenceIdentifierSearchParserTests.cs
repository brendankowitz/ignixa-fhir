// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Globalization;
using Ignixa.Search;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class ReferenceIdentifierSearchParserTests
{
    private static readonly string[] EncounterResourceType = ["Encounter"];
    private static readonly string[] PatientResourceType = ["Patient"];

    [Theory]
    [InlineData("http://example.org/facilityA|1234", "http://example.org/facilityA", "1234")]
    [InlineData("1234", null, "1234")]
    [InlineData("|1234", "", "1234")]
    [InlineData("http://example.org/facilityA|", "http://example.org/facilityA", "")]
    public void GivenReferenceIdentifierModifier_WhenParsing_ThenBindsDerivedTokenParameter(
        string value,
        string? expectedSystem,
        string expectedCode)
    {
        // Arrange
        var context = new SearchParserTestContext();
        SearchParameterInfo patient = context.Add(
            "Encounter",
            "patient",
            SearchParamType.Reference,
            targets: PatientResourceType);
        SearchParameterInfo derived = context.AddIdentifierDerivative(patient);

        // Act
        Expression expression = context.Parser.Parse(EncounterResourceType, "patient:identifier", value);

        // Assert
        var searchParameter = expression.ShouldBeOfType<SearchParameterExpression>();
        searchParameter.Parameter.ShouldBeSameAs(derived);
        var predicate = searchParameter.Expression.ShouldBeOfType<SearchParameterPredicateExpression>();
        predicate.Parameter.ShouldBeSameAs(derived);
        predicate.Modifier.ShouldBeNull();
        var token = predicate.Value.ShouldBeOfType<TokenSearchValue>();
        token.System.ShouldBe(expectedSystem);
        token.Code.ShouldBe(expectedCode);
    }

    [Theory]
    [InlineData(SearchParamType.String, "Smith")]
    [InlineData(SearchParamType.Token, "1234")]
    [InlineData(SearchParamType.Date, "2024-01-01")]
    public void GivenNonReferenceIdentifierModifier_WhenParsing_ThenThrowsModifierNotSupported(
        SearchParamType searchParamType,
        string value)
    {
        // Arrange
        var context = new SearchParserTestContext();
        SearchParameterInfo searchParameter = context.Add("Encounter", "parameter", searchParamType);

        // Act
        SearchModifierNotSupportedException exception = Should.Throw<SearchModifierNotSupportedException>(
            () => context.Parser.Parse(EncounterResourceType, "parameter:identifier", value));

        // Assert
        exception.Message.ShouldBe(string.Format(
            CultureInfo.InvariantCulture,
            Resources.ModifierNotSupported,
            new SearchModifier(SearchModifierCode.Identifier),
            searchParameter.Code));
    }

    [Fact]
    public void GivenReferenceIdentifierModifierWithoutDerivedParameter_WhenParsing_ThenThrowsModifierNotSupported()
    {
        // Arrange
        var context = new SearchParserTestContext();
        SearchParameterInfo patient = context.Add(
            "Encounter",
            "patient",
            SearchParamType.Reference,
            targets: PatientResourceType);

        // Act
        SearchModifierNotSupportedException exception = Should.Throw<SearchModifierNotSupportedException>(
            () => context.Parser.Parse(EncounterResourceType, "patient:identifier", "http://example.org/facilityA|1234"));

        // Assert
        exception.Message.ShouldBe(string.Format(
            CultureInfo.InvariantCulture,
            Resources.ModifierNotSupported,
            new SearchModifier(SearchModifierCode.Identifier),
            patient.Code));
    }

    [Fact]
    public void GivenReferenceChain_WhenParsingIdentifierOnTarget_ThenRemainsChained()
    {
        // Arrange
        var context = new SearchParserTestContext();
        SearchParameterInfo patient = context.Add(
            "Encounter",
            "patient",
            SearchParamType.Reference,
            targets: PatientResourceType);
        SearchParameterInfo identifier = context.Add("Patient", "identifier", SearchParamType.Token);

        // Act
        Expression expression = context.Parser.Parse(
            EncounterResourceType,
            "patient.identifier",
            "http://example.org/facilityA|1234");

        // Assert
        var chain = expression.ShouldBeOfType<ChainedExpression>();
        chain.ReferenceSearchParameter.ShouldBeSameAs(patient);
        var target = chain.Expression.ShouldBeOfType<SearchParameterExpression>();
        target.Parameter.ShouldBeSameAs(identifier);
    }

    [Fact]
    public void GivenReverseChainIdentifierParameter_WhenParsing_ThenRemainsTargetSearch()
    {
        // Arrange
        var context = new SearchParserTestContext();
        context.Add(
            "Encounter",
            "patient",
            SearchParamType.Reference,
            targets: PatientResourceType);
        SearchParameterInfo identifier = context.Add("Encounter", "identifier", SearchParamType.Token);

        // Act
        Expression expression = context.Parser.Parse(
            PatientResourceType,
            "_has:Encounter:patient:identifier",
            "http://example.org/facilityA|1234");

        // Assert
        var chain = expression.ShouldBeOfType<ChainedExpression>();
        chain.Reversed.ShouldBeTrue();
        var target = chain.Expression.ShouldBeOfType<SearchParameterExpression>();
        target.Parameter.ShouldBeSameAs(identifier);
        var predicate = target.Expression.ShouldBeOfType<SearchParameterPredicateExpression>();
        predicate.Modifier.ShouldBeNull();
        predicate.Value.ShouldBeOfType<TokenSearchValue>();
    }
}
