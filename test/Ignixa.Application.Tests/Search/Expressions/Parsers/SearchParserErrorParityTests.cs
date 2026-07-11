// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Indexing;
using Ignixa.Specification.ValueSets.Normative;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchParserErrorParityTests
{
    [Theory]
    [InlineData("patient..name", "search key", 9)]
    [InlineData("name:exact:contains", "search key", 11)]
    public void GivenMalformedKey_WhenParsing_ThenReportsSyntaxPosition(
        string key,
        string subject,
        int column)
    {
        var context = new SearchParserTestContext();

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => context.Parser.Parse(["Patient"], key, "value"));

        exception.Message.ShouldContain(subject);
        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain($"column {column}");
    }

    [Fact]
    public void GivenTrailingValueEscape_WhenParsing_ThenReportsEscapePosition()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => context.Parser.Parse(["Patient"], "name", @"value\"));

        exception.Message.ShouldContain("search value");
        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column 6");
    }

    [Fact]
    public void GivenUnsupportedParameter_WhenBinding_ThenPreservesExceptionCategory()
    {
        var context = new SearchParserTestContext();
        context.DefinitionManager
            .GetSearchParameter("Patient", "unknown")
            .Returns(_ => throw new SearchParameterNotSupportedException(
                "Patient",
                "unknown"));

        Should.Throw<SearchParameterNotSupportedException>(
            () => context.Parser.Parse(["Patient"], "unknown", "value"));
    }

    [Fact]
    public void GivenInvalidMissingValue_WhenParsing_ThenPreservesResourceMessage()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => context.Parser.Parse(
                ["Patient"],
                "name:missing",
                "yes"));

        exception.Message.ShouldBe(Resources.InvalidValueTypeForMissingModifier);
    }

    [Fact]
    public void GivenInvalidAtomicValue_WhenParsing_ThenPreservesBadRequestCategory()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "birthdate", SearchParamType.Date);

        Should.Throw<BadSearchRequestException>(
            () => context.Parser.Parse(
                ["Patient"],
                "birthdate",
                "not-a-date"));
    }

    [Fact]
    public void GivenInvalidOfTypeComponent_WhenParsing_ThenPreservesBadRequestCategory()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "identifier", SearchParamType.Token);

        Should.Throw<BadSearchRequestException>(
            () => context.Parser.Parse(
                ["Patient"],
                "identifier:of-type",
                "|MR|"));
    }

    [Theory]
    [InlineData(false, "_include")]
    [InlineData(true, "_revinclude")]
    public void GivenEmptyIncludeTarget_WhenParsing_ThenPreservesResourceMessage(
        bool isReversed,
        string parameterName)
    {
        var context = new SearchParserTestContext();

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => context.Parser.ParseInclude(
                ["Patient"],
                "Observation:subject:",
                isReversed,
                iterate: false));

        exception.Message.ShouldBe(string.Format(
            Resources.IncludeInvalidTargetResourceType,
            parameterName,
            "Observation",
            "subject",
            "<empty>"));
    }
}
