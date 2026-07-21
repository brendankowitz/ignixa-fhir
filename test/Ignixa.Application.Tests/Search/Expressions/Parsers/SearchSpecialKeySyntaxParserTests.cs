// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchSpecialKeySyntaxParserTests
{
    [Theory]
    [InlineData("Observation:subject", "Observation", "subject", null, false)]
    [InlineData("Observation:subject:Patient", "Observation", "subject", "Patient", false)]
    [InlineData("Observation:*", "Observation", null, null, true)]
    [InlineData("*:*", "*", null, null, true)]
    public void GivenIncludeSyntax_WhenParsing_ThenReturnsExpectedSyntax(
        string value,
        string expectedSourceResourceType,
        string? expectedSearchParameterName,
        string? expectedTargetResourceType,
        bool expectedWildcard)
    {
        IncludeKeySyntax syntax = SearchKeySyntaxParser.ParseInclude(value);

        syntax.SourceResourceType.ShouldBe(expectedSourceResourceType);
        syntax.SearchParameterName.ShouldBe(expectedSearchParameterName);
        syntax.TargetResourceType.ShouldBe(expectedTargetResourceType);
        syntax.Wildcard.ShouldBe(expectedWildcard);
    }

    [Theory]
    [InlineData("*:*", null, null)]
    [InlineData("Observation:*", "Observation", null)]
    [InlineData("Observation:subject", "Observation", "subject")]
    public void GivenNotReferencedSyntax_WhenParsing_ThenReturnsExpectedSyntax(
        string value,
        string? expectedSourceResourceType,
        string? expectedReferencePath)
    {
        NotReferencedKeySyntax syntax = SearchKeySyntaxParser.ParseNotReferenced(value);

        syntax.SourceResourceType.ShouldBe(expectedSourceResourceType);
        syntax.ReferencePath.ShouldBe(expectedReferencePath);
    }

    [Theory]
    [InlineData("Observation:")]
    [InlineData("Observation:subject.name")]
    [InlineData("Observation:subject:extra")]
    [InlineData("Observation:subject name")]
    public void GivenMalformedNotReferencedSyntax_WhenParsing_ThenThrowsPositionedInvalidSearchOperation(
        string value)
    {
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchKeySyntaxParser.ParseNotReferenced(value));

        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column");
    }

    [Fact]
    public void GivenIncludeWithTrailingTargetColon_WhenParsing_ThenThrowsPositionedInvalidSearchOperation()
    {
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchKeySyntaxParser.ParseInclude("Observation:subject:"));

        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column 21");
    }
}
