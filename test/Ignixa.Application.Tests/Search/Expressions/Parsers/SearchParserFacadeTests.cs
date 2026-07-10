// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchParserFacadeTests
{
    [Fact]
    public void GivenNestedMixedChain_WhenParsingPublicFacade_ThenBuildsCurrentAst()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "patient", SearchParamType.Reference, ["Patient"]);
        context.Add("Group", "member", SearchParamType.Reference, ["Patient"]);
        context.Add("Group", "_tag", SearchParamType.Token);
        IExpressionParser parser = context.Parser;

        var result = parser.Parse(
            ["Observation"],
            "patient:Patient._has:Group:member:_tag",
            "http://example.org|reviewed");

        var forward = result.ShouldBeOfType<ChainedExpression>();
        forward.Reversed.ShouldBeFalse();
        var reverse = forward.Expression.ShouldBeOfType<ChainedExpression>();
        reverse.Reversed.ShouldBeTrue();
        reverse.ResourceTypes.ShouldBe(["Group"]);
        reverse.TargetResourceTypes.ShouldBe(["Patient"]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GivenIncludeFlags_WhenParsingPublicFacade_ThenPreservesFlags(
        bool reversed,
        bool iterate)
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, ["Patient"]);

        var result = context.Parser.ParseInclude(
            ["Patient"],
            "Observation:subject:Patient",
            reversed,
            iterate);

        result.SourceResourceType.ShouldBe("Observation");
        result.TargetResourceType.ShouldBe("Patient");
        result.Reversed.ShouldBe(reversed);
        result.Iterate.ShouldBe(iterate);
    }

    [Fact]
    public void GivenWildcardInclude_WhenParsingPublicFacade_ThenCollectsDistinctTargets()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, ["Patient"]);
        context.Add("Observation", "encounter", SearchParamType.Reference, ["Encounter", "Patient"]);

        var result = context.Parser.ParseInclude(
            ["Observation"],
            "Observation:*",
            isReversed: false,
            iterate: false);

        result.WildCard.ShouldBeTrue();
        result.ReferencedTypes.ShouldBe(["Patient", "Encounter"], ignoreOrder: true);
    }
}
