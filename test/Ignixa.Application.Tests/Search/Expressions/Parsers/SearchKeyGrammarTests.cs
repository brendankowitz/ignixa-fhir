// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchKeyGrammarTests
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
        var syntax = SearchKeyGrammar.ParseParameter(key);

        var parameter = syntax.ShouldBeOfType<ParameterKeySyntax>();
        parameter.Name.ShouldBe(expectedName);
        parameter.Modifier.ShouldBe(expectedModifier);
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
        var syntax = SearchKeyGrammar.ParseParameter(key);

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
        var syntax = SearchKeyGrammar.ParseParameter("_has:Observation:subject:code");

        var chain = syntax.ShouldBeOfType<ReverseChainKeySyntax>();
        chain.SourceResourceType.ShouldBe("Observation");
        chain.ReferenceName.ShouldBe("subject");

        var next = chain.Next.ShouldBeOfType<ParameterKeySyntax>();
        next.Name.ShouldBe("code");
        next.Modifier.ShouldBeNull();
    }

    [Fact]
    public void GivenMixedChain_WhenParsing_ThenReturnsNestedForwardAndReverseChains()
    {
        var syntax = SearchKeyGrammar.ParseParameter("patient:Patient._has:Group:member:_tag");

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
}
