// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchValueTokenizerTests
{
    [Fact]
    public void GivenEmptyInput_WhenTokenizing_ThenReturnsEmptyTokenList()
    {
        var result = SearchValueTokenizer.Instance.TryTokenize(string.Empty);

        result.HasValue.ShouldBeTrue(result.ToString());
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public void GivenEscapedAndUnescapedSeparators_WhenTokenizing_ThenOnlyUnescapedSeparatorsAreStructural()
    {
        var result = SearchValueTokenizer.Instance.TryTokenize(@"a\,b\$c\|d\\e,f$g|h");

        result.HasValue.ShouldBeTrue(result.ToString());
        result.Value.Select(token => (token.Kind, token.ToStringValue())).ShouldBe(
        [
            (SearchValueTokenKind.Text, @"a\,b\$c\|d\\e"),
            (SearchValueTokenKind.Comma, ","),
            (SearchValueTokenKind.Text, "f"),
            (SearchValueTokenKind.Dollar, "$"),
            (SearchValueTokenKind.Text, "g"),
            (SearchValueTokenKind.Pipe, "|"),
            (SearchValueTokenKind.Text, "h"),
        ]);
    }

    [Theory]
    [InlineData(@"\", 1)]
    [InlineData(@"\q", 1)]
    [InlineData(@"value\", 6)]
    [InlineData(@"value\q", 6)]
    public void GivenInvalidEscape_WhenTokenizing_ThenReturnsPositionedFailure(string value, int expectedColumn)
    {
        var result = SearchValueTokenizer.Instance.TryTokenize(value);

        result.HasValue.ShouldBeFalse();
        result.ErrorPosition.Line.ShouldBe(1);
        result.ErrorPosition.Column.ShouldBe(expectedColumn);
        result.Expectations.ShouldContain("valid FHIR escape");
    }
}
