// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchValueGrammarTests
{
    [Theory]
    [InlineData(SearchParamType.String, "gtSmith", SearchComparator.Eq, "gtSmith")]
    [InlineData(SearchParamType.Date, "gt2026-07-10", SearchComparator.Gt, "2026-07-10")]
    [InlineData(SearchParamType.Number, "le120", SearchComparator.Le, "120")]
    [InlineData(SearchParamType.Quantity, "ap5.4|mg", SearchComparator.Ap, "5.4|mg")]
    public void GivenScalarValue_WhenParsing_ThenComparatorDependsOnSearchType(
        SearchParamType type,
        string value,
        SearchComparator comparator,
        string rawText)
    {
        var result = SearchValueGrammar.Parse(type, null, value);

        var atomic = result.ShouldBeOfType<AtomicValueSyntax>();
        atomic.Comparator.ShouldBe(comparator);
        atomic.RawText.ShouldBe(rawText);
    }

    [Fact]
    public void GivenEscapedCommaAlternatives_WhenParsing_ThenPreservesEscapedText()
    {
        var result = SearchValueGrammar.Parse(
            SearchParamType.Token,
            null,
            @"system|a\,b,system|c");

        var alternatives = result.ShouldBeOfType<AlternativesValueSyntax>();
        alternatives.Items.Length.ShouldBe(2);
        alternatives.Items[0].ShouldBeOfType<AtomicValueSyntax>()
            .RawText.ShouldBe(@"system|a\,b");
        alternatives.Items[1].ShouldBeOfType<AtomicValueSyntax>()
            .RawText.ShouldBe("system|c");
    }

    [Fact]
    public void GivenCompositeAlternatives_WhenParsing_ThenBuildsComponentsBeforeAlternatives()
    {
        var result = SearchValueGrammar.Parse(
            SearchParamType.Composite,
            null,
            "http://loinc.org|8480-6$gt120,29463-7$lt80");

        var alternatives = result.ShouldBeOfType<AlternativesValueSyntax>();
        var first = alternatives.Items[0].ShouldBeOfType<CompositeValueSyntax>();
        first.Components[0].RawText.ShouldBe("http://loinc.org|8480-6");
        first.Components[1].Comparator.ShouldBe(SearchComparator.Gt);
        first.Components[1].RawText.ShouldBe("120");
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("TRUE", true)]
    public void GivenMissingModifier_WhenParsing_ThenBuildsBooleanSyntax(string value, bool expected)
    {
        var result = SearchValueGrammar.Parse(
            SearchParamType.String,
            new SearchModifier(SearchModifierCode.Missing),
            value);

        result.ShouldBe(new MissingValueSyntax(expected));
    }

    [Fact]
    public void GivenMissingModifierWithInvalidBoolean_WhenParsing_ThenRejectsSyntax()
    {
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchValueGrammar.Parse(
                SearchParamType.String,
                new SearchModifier(SearchModifierCode.Missing),
                "1"));

        exception.Message.ShouldBe(Resources.InvalidValueTypeForMissingModifier);
    }

    [Fact]
    public void GivenOfTypeModifier_WhenParsing_ThenBuildsTripletSyntax()
    {
        var result = SearchValueGrammar.Parse(
            SearchParamType.Token,
            new SearchModifier(SearchModifierCode.OfType),
            "http://terminology.hl7.org|MR|123");

        result.ShouldBe(new OfTypeValueSyntax(
            "http://terminology.hl7.org",
            "MR",
            "123"));
    }

    [Fact]
    public void GivenOfTypeAlternatives_WhenParsing_ThenBuildsAlternativeTriplets()
    {
        var result = SearchValueGrammar.Parse(
            SearchParamType.Token,
            new SearchModifier(SearchModifierCode.OfType),
            "http://terminology.hl7.org|MR|123,http://terminology.hl7.org|SS|456");

        var alternatives = result.ShouldBeOfType<AlternativesValueSyntax>();
        alternatives.Items.Count(item => item is OfTypeValueSyntax).ShouldBe(2);
    }

    [Fact]
    public void GivenTextModifierWithComma_WhenParsing_ThenPreservesCommaAsText()
    {
        var result = SearchValueGrammar.Parse(
            SearchParamType.Token,
            new SearchModifier(SearchModifierCode.Text),
            "alpha,beta");

        result.ShouldBe(new AtomicValueSyntax("alpha,beta", SearchComparator.Eq));
    }

    [Fact]
    public void GivenOfTypeModifierWithDollarInSegments_WhenParsing_ThenPreservesDollar()
    {
        var result = SearchValueGrammar.Parse(
            SearchParamType.Token,
            new SearchModifier(SearchModifierCode.OfType),
            "http://hl7.org/fhir/OperationDefinition/$member-match|MR|123$abc");

        result.ShouldBe(new OfTypeValueSyntax(
            "http://hl7.org/fhir/OperationDefinition/$member-match",
            "MR",
            "123$abc"));
    }

    [Theory]
    [InlineData("value")]
    [InlineData("system|code")]
    [InlineData("system|code|value|extra")]
    public void GivenOfTypeModifierWithInvalidArity_WhenParsing_ThenRejectsSyntax(string value)
    {
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchValueGrammar.Parse(
                SearchParamType.Token,
                new SearchModifier(SearchModifierCode.OfType),
                value));

        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column");
    }

    [Theory]
    [InlineData(SearchParamType.String)]
    [InlineData(SearchParamType.Date)]
    [InlineData(SearchParamType.Composite)]
    public void GivenEmptyValue_WhenParsing_ThenRejectsSyntax(SearchParamType type)
    {
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchValueGrammar.Parse(type, null, string.Empty));

        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column");
    }

    [Theory]
    [InlineData(SearchParamType.Token, "a,,b")]
    [InlineData(SearchParamType.Composite, "a$$b")]
    [InlineData(SearchParamType.Composite, "$a")]
    [InlineData(SearchParamType.Composite, "a$")]
    public void GivenEmptyValuePart_WhenParsing_ThenRejectsSyntax(
        SearchParamType type,
        string value)
    {
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchValueGrammar.Parse(type, null, value));

        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column");
    }
}
