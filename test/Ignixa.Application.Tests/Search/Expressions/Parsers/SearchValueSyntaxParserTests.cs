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

public class SearchValueSyntaxParserTests
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
        var result = SearchValueSyntaxParser.Parse(type, null, value);

        var atomic = result.ShouldBeOfType<AtomicValueSyntax>();
        atomic.Comparator.ShouldBe(comparator);
        atomic.RawText.ShouldBe(rawText);
    }

    [Fact]
    public void GivenEscapedCommaAlternatives_WhenParsing_ThenPreservesEscapedText()
    {
        var result = SearchValueSyntaxParser.Parse(
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
        var result = SearchValueSyntaxParser.Parse(
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
        var result = SearchValueSyntaxParser.Parse(
            SearchParamType.String,
            new SearchModifier(SearchModifierCode.Missing),
            value);

        result.ShouldBe(new MissingValueSyntax(expected));
    }

    [Fact]
    public void GivenMissingModifierWithInvalidBoolean_WhenParsing_ThenRejectsSyntax()
    {
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchValueSyntaxParser.Parse(
                SearchParamType.String,
                new SearchModifier(SearchModifierCode.Missing),
                "1"));

        exception.Message.ShouldBe(Resources.InvalidValueTypeForMissingModifier);
    }

    [Fact]
    public void GivenOfTypeModifier_WhenParsing_ThenBuildsTripletSyntax()
    {
        var result = SearchValueSyntaxParser.Parse(
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
        var result = SearchValueSyntaxParser.Parse(
            SearchParamType.Token,
            new SearchModifier(SearchModifierCode.OfType),
            "http://terminology.hl7.org|MR|123,http://terminology.hl7.org|SS|456");

        var alternatives = result.ShouldBeOfType<AlternativesValueSyntax>();
        alternatives.Items.Count(item => item is OfTypeValueSyntax).ShouldBe(2);
    }

    [Fact]
    public void GivenTextModifierWithComma_WhenParsing_ThenPreservesCommaAsText()
    {
        var result = SearchValueSyntaxParser.Parse(
            SearchParamType.Token,
            new SearchModifier(SearchModifierCode.Text),
            "alpha,beta");

        result.ShouldBe(new AtomicValueSyntax("alpha,beta", SearchComparator.Eq));
    }

    [Fact]
    public void GivenOfTypeModifierWithDollarInSegments_WhenParsing_ThenPreservesDollar()
    {
        var result = SearchValueSyntaxParser.Parse(
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
            () => SearchValueSyntaxParser.Parse(
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
            () => SearchValueSyntaxParser.Parse(type, null, string.Empty));

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
            () => SearchValueSyntaxParser.Parse(type, null, value));

        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column");
    }

    [Theory]
    [InlineData(@"\", 1)]
    [InlineData(@"\q", 1)]
    [InlineData(@"value\", 6)]
    [InlineData(@"value\q", 6)]
    public void GivenInvalidEscape_WhenParsingStringValue_ThenRejectsWithExactPosition(
        string value,
        int expectedColumn)
    {
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchValueSyntaxParser.Parse(SearchParamType.String, null, value));

        exception.Message.ShouldContain("Malformed search value");
        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain($"column {expectedColumn}:");
        exception.Message.ShouldContain("valid FHIR escape");
    }

    [Fact]
    public void GivenEscapedTokenSeparators_WhenParsing_ThenReturnsSingleAtomicValue()
    {
        const string value = @"a\,b\$c\|d\\e";

        var result = SearchValueSyntaxParser.Parse(SearchParamType.Token, null, value);

        var atomic = result.ShouldBeOfType<AtomicValueSyntax>();
        atomic.RawText.ShouldBe(value);
        atomic.Comparator.ShouldBe(SearchComparator.Eq);
    }

    [Fact]
    public void GivenEscapedCompositeSeparator_WhenParsing_ThenReturnsSingleComponent()
    {
        var result = SearchValueSyntaxParser.Parse(SearchParamType.Composite, null, @"code\$value");

        var composite = result.ShouldBeOfType<CompositeValueSyntax>();
        composite.Components.Length.ShouldBe(1);
        composite.Components[0].Comparator.ShouldBe(SearchComparator.Eq);
        composite.Components[0].RawText.ShouldBe(@"code\$value");
    }

    [Fact]
    public void GivenTokenStartingWithComparatorText_WhenParsing_ThenTreatsComparatorAsText()
    {
        var result = SearchValueSyntaxParser.Parse(SearchParamType.Token, null, "gtcode");

        var atomic = result.ShouldBeOfType<AtomicValueSyntax>();
        atomic.RawText.ShouldBe("gtcode");
        atomic.Comparator.ShouldBe(SearchComparator.Eq);
    }

    [Fact]
    public void GivenTextModifierWithAllSeparators_WhenParsing_ThenPreservesSeparatorsAsText()
    {
        const string value = "alpha,beta$gamma|delta";

        var result = SearchValueSyntaxParser.Parse(
            SearchParamType.Token,
            new SearchModifier(SearchModifierCode.Text),
            value);

        var atomic = result.ShouldBeOfType<AtomicValueSyntax>();
        atomic.RawText.ShouldBe(value);
        atomic.Comparator.ShouldBe(SearchComparator.Eq);
    }

    [Fact]
    public void GivenOfTypeModifierWithEscapedPipe_WhenParsing_ThenPreservesEscapedSystem()
    {
        var result = SearchValueSyntaxParser.Parse(
            SearchParamType.Token,
            new SearchModifier(SearchModifierCode.OfType),
            @"http://example.org\|v2|MR|123");

        result.ShouldBe(new OfTypeValueSyntax(
            @"http://example.org\|v2",
            "MR",
            "123"));
    }

    [Theory]
    [InlineData("a$$b", 3)]
    [InlineData("$a", 1)]
    [InlineData("a$", 3)]
    public void GivenEmptyCompositeComponent_WhenParsing_ThenRejectsWithExactPosition(
        string value,
        int expectedColumn)
    {
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchValueSyntaxParser.Parse(SearchParamType.Composite, null, value));

        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain($"column {expectedColumn}:");
    }
}
