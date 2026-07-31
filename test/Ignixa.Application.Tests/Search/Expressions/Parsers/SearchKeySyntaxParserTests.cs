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

public class SearchKeySyntaxParserTests
{
    [Theory]
    [InlineData("name", "name", null)]
    [InlineData("name:exact", "name", "exact")]
    [InlineData("identifier:of-type", "identifier", "of-type")]
    public void GivenTerminalKey_WhenParsing_ThenReturnsParameterKeySyntax(
        string key,
        string expectedName,
        string? expectedModifier)
    {
        var syntax = SearchKeySyntaxParser.ParseParameter(key);

        var parameter = syntax.ShouldBeOfType<ParameterKeySyntax>();
        parameter.Name.ShouldBe(expectedName);
        parameter.Modifier.ShouldBe(expectedModifier);
    }

    [Theory]
    [InlineData("07d5d21479ee52Code", "07d5d21479ee52Code", null)]
    [InlineData("2ndline:exact", "2ndline", "exact")]
    [InlineData("0", "0", null)]
    public void GivenAParameterCodeStartingWithADigit_WhenParsing_ThenItIsAcceptedAsAnIdentifier(
        string key,
        string expectedName,
        string? expectedModifier)
    {
        // FHIR types SearchParameter.code as `code`, whose regex ([^\s]+([\s]?[^\s]+)*) admits any
        // non-whitespace first character, so a custom search parameter is free to start with a digit.
        // Rejecting it as a syntax error turns a valid registered parameter into an unparseable key.
        var syntax = SearchKeySyntaxParser.ParseParameter(key);

        var parameter = syntax.ShouldBeOfType<ParameterKeySyntax>();
        parameter.Name.ShouldBe(expectedName);
        parameter.Modifier.ShouldBe(expectedModifier);
    }

    [Fact]
    public void GivenADigitLeadingResourceTypeInAChain_WhenParsing_ThenItIsDeferredToTheBinder()
    {
        // A digit-leading token in a resource-type position is a name error, not a syntax error --
        // the parser accepts it and the binder rejects it, which is what produces a usable diagnostic.
        var syntax = SearchKeySyntaxParser.ParseParameter("subject:2Foo.name");

        var chain = syntax.ShouldBeOfType<ForwardChainKeySyntax>();
        chain.ReferenceName.ShouldBe("subject");
        chain.TargetResourceType.ShouldBe("2Foo");
    }

    [Fact]
    public void GivenADigitLeadingModifier_WhenParsing_ThenItIsAcceptedAsAnIdentifier()
    {
        var syntax = SearchKeySyntaxParser.ParseParameter("name:0exact");

        var parameter = syntax.ShouldBeOfType<ParameterKeySyntax>();
        parameter.Name.ShouldBe("name");
        parameter.Modifier.ShouldBe("0exact");
    }

    [Fact]
    public void GivenADigitLeadingParameterInAChain_WhenParsing_ThenBothSegmentsAreAccepted()
    {
        var syntax = SearchKeySyntaxParser.ParseParameter("subject.07code");

        var chain = syntax.ShouldBeOfType<ForwardChainKeySyntax>();
        chain.ReferenceName.ShouldBe("subject");
        chain.Next.ShouldBeOfType<ParameterKeySyntax>().Name.ShouldBe("07code");
    }

    [Fact]
    public void GivenADigitLeadingReferencePathInNotReferenced_WhenParsing_ThenItIsAcceptedAsAnIdentifier()
    {
        // The reference path is an identifier position like any other; gating it on a leading letter made
        // it the one place a digit-leading custom parameter was a syntax error rather than a name error.
        var syntax = SearchKeySyntaxParser.ParseNotReferenced("Observation:0subject");

        syntax.SourceResourceType.ShouldBe("Observation");
        syntax.ReferencePath.ShouldBe("0subject");
    }

    [Fact]
    public void GivenADigitLeadingSourceTypeInNotReferenced_WhenParsing_ThenItIsDeferredToTheBinder()
    {
        var syntax = SearchKeySyntaxParser.ParseNotReferenced("2Observation:subject");

        syntax.SourceResourceType.ShouldBe("2Observation");
        syntax.ReferencePath.ShouldBe("subject");
    }

    [Fact]
    public void GivenADigitLeadingSourceTypeInAnInclude_WhenParsing_ThenItIsDeferredToTheBinder()
    {
        var syntax = SearchKeySyntaxParser.ParseInclude("2Observation:0subject:3Patient");

        syntax.SourceResourceType.ShouldBe("2Observation");
        syntax.SearchParameterName.ShouldBe("0subject");
        syntax.TargetResourceType.ShouldBe("3Patient");
        syntax.Wildcard.ShouldBeFalse();
    }

    [Theory]
    [InlineData("subject.name", "subject", null, "name")]
    [InlineData("subject:Patient.name", "subject", "Patient", "name")]
    public void GivenForwardKey_WhenParsing_ThenReturnsForwardChainKeySyntax(
        string key,
        string expectedReferenceName,
        string? expectedTargetResourceType,
        string expectedNextName)
    {
        var syntax = SearchKeySyntaxParser.ParseParameter(key);

        var chain = syntax.ShouldBeOfType<ForwardChainKeySyntax>();
        chain.ReferenceName.ShouldBe(expectedReferenceName);
        chain.TargetResourceType.ShouldBe(expectedTargetResourceType);

        var next = chain.Next.ShouldBeOfType<ParameterKeySyntax>();
        next.Name.ShouldBe(expectedNextName);
        next.Modifier.ShouldBeNull();
    }

    [Fact]
    public void GivenReverseKey_WhenParsing_ThenReturnsReverseChainKeySyntax()
    {
        var syntax = SearchKeySyntaxParser.ParseParameter("_has:Observation:subject:code");

        var chain = syntax.ShouldBeOfType<ReverseChainKeySyntax>();
        chain.SourceResourceType.ShouldBe("Observation");
        chain.ReferenceName.ShouldBe("subject");

        var next = chain.Next.ShouldBeOfType<ParameterKeySyntax>();
        next.Name.ShouldBe("code");
        next.Modifier.ShouldBeNull();
    }

    [Fact]
    public void GivenADigitLeadingSegmentInAReverseChain_WhenParsing_ThenBothSegmentsAreAccepted()
    {
        // The _has: path parses its source type and reference name through the same identifier rules,
        // so the loosened start character must apply there too rather than only to a terminal name.
        var syntax = SearchKeySyntaxParser.ParseParameter("_has:2Observation:0subject:code");

        var chain = syntax.ShouldBeOfType<ReverseChainKeySyntax>();
        chain.SourceResourceType.ShouldBe("2Observation");
        chain.ReferenceName.ShouldBe("0subject");
        chain.Next.ShouldBeOfType<ParameterKeySyntax>().Name.ShouldBe("code");
    }

    [Fact]
    public void GivenReverseLikeForwardChain_WhenParsing_ThenBacktracksToOrdinaryForwardSyntax()
    {
        var syntax = SearchKeySyntaxParser.ParseParameter("_has:foo.bar:baz.qux:quux");

        var outer = syntax.ShouldBeOfType<ForwardChainKeySyntax>();
        outer.ReferenceName.ShouldBe("_has");
        outer.TargetResourceType.ShouldBe("foo");

        var next = outer.Next.ShouldBeOfType<ForwardChainKeySyntax>();
        next.ReferenceName.ShouldBe("bar");
        next.TargetResourceType.ShouldBe("baz");

        var terminal = next.Next.ShouldBeOfType<ParameterKeySyntax>();
        terminal.Name.ShouldBe("qux");
        terminal.Modifier.ShouldBe("quux");
    }

    [Fact]
    public void GivenRecursiveReverseKey_WhenParsing_ThenReturnsNestedReverseChainKeySyntax()
    {
        var syntax = SearchKeySyntaxParser.ParseParameter("_has:Observation:subject:_has:Group:member:_tag");

        var outer = syntax.ShouldBeOfType<ReverseChainKeySyntax>();
        outer.SourceResourceType.ShouldBe("Observation");
        outer.ReferenceName.ShouldBe("subject");

        var next = outer.Next.ShouldBeOfType<ReverseChainKeySyntax>();
        next.SourceResourceType.ShouldBe("Group");
        next.ReferenceName.ShouldBe("member");

        var terminal = next.Next.ShouldBeOfType<ParameterKeySyntax>();
        terminal.Name.ShouldBe("_tag");
        terminal.Modifier.ShouldBeNull();
    }

    [Fact]
    public void GivenMixedChain_WhenParsing_ThenReturnsNestedForwardAndReverseChains()
    {
        var syntax = SearchKeySyntaxParser.ParseParameter("patient:Patient._has:Group:member:_tag");

        var forward = syntax.ShouldBeOfType<ForwardChainKeySyntax>();
        forward.ReferenceName.ShouldBe("patient");
        forward.TargetResourceType.ShouldBe("Patient");

        var reverse = forward.Next.ShouldBeOfType<ReverseChainKeySyntax>();
        reverse.SourceResourceType.ShouldBe("Group");
        reverse.ReferenceName.ShouldBe("member");

        var terminal = reverse.Next.ShouldBeOfType<ParameterKeySyntax>();
        terminal.Name.ShouldBe("_tag");
        terminal.Modifier.ShouldBeNull();
    }

    [Fact]
    public void GivenAWildcardReferencePathInNotReferenced_WhenParsing_ThenTheReferencePathIsNull()
    {
        var syntax = SearchKeySyntaxParser.ParseNotReferenced("Observation:*");

        syntax.SourceResourceType.ShouldBe("Observation");
        syntax.ReferencePath.ShouldBeNull();
    }

    [Theory]
    [InlineData("Observation:")]
    [InlineData("Observation")]
    [InlineData(":subject")]
    [InlineData("Observation:subject:extra")]
    public void GivenMalformedNotReferencedValue_WhenParsing_ThenThrowsPositionedSyntaxError(string value)
    {
        // "Observation:" is the at-end case: the reference path delegates to ParseIdentifier, which is what
        // reports the missing identifier now that ParseNotReferencedPath does not test for the end itself.
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchKeySyntaxParser.ParseNotReferenced(value));

        exception.Message.ShouldContain("_not-referenced value");
        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column");
    }

    [Theory]
    [InlineData("Observation")]
    [InlineData("Observation:")]
    [InlineData("Observation:subject:")]
    [InlineData(":subject")]
    public void GivenMalformedIncludeKey_WhenParsing_ThenThrowsPositionedSyntaxError(string key)
    {
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchKeySyntaxParser.ParseInclude(key));

        exception.Message.ShouldContain("include key");
        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column");
    }

    [Theory]
    [InlineData("")]
    [InlineData(".name")]
    [InlineData("patient..name")]
    [InlineData("name:exact:contains")]
    [InlineData("_has:Observation:subject")]
    [InlineData("_has::subject:code")]
    public void GivenMalformedParameterKey_WhenParsing_ThenThrowsPositionedSyntaxError(string key)
    {
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchKeySyntaxParser.ParseParameter(key));

        exception.Message.ShouldContain("Malformed search key");
        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column");
    }
}
