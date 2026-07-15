// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class ExpressionParserCharacterizationTests
{
    private static readonly string[] PatientResourceType = ["Patient"];
    private static readonly string[] ObservationResourceType = ["Observation"];
    private static readonly string[] GroupResourceType = ["Group"];

    [Fact]
    public void GivenPatientStringSearch_WhenParsingNameEqualsSmith_ThenReturnsStartsWithExpression()
    {
        var context = new SearchParserTestContext();
        var searchParameter = context.Add("Patient", "name", SearchParamType.String);

        var expression = context.Parser.Parse(PatientResourceType, "name", "Smith");

        var searchParameterExpression = expression.ShouldBeOfType<SearchParameterExpression>();
        searchParameterExpression.Parameter.ShouldBeSameAs(searchParameter);

        var stringExpression = searchParameterExpression.Expression.ShouldBeOfType<StringExpression>();
        stringExpression.StringOperator.ShouldBe(StringOperator.StartsWith);
        stringExpression.FieldName.ShouldBe(FieldName.String);
        stringExpression.Value.ShouldBe("Smith");
        stringExpression.IgnoreCase.ShouldBeTrue();
    }

    [Fact]
    public void GivenObservationReferenceChainWithNestedReverseChain_WhenParsingPatientHasGroupMemberTag_ThenBuildsExpectedChainMetadata()
    {
        var context = new SearchParserTestContext();
        var observationPatient = context.Add("Observation", "patient", SearchParamType.Reference, targets: PatientResourceType);
        var groupMember = context.Add("Group", "member", SearchParamType.Reference, targets: PatientResourceType);
        var groupTag = context.Add("Group", "_tag", SearchParamType.Token);

        var expression = context.Parser.Parse(ObservationResourceType, "patient:Patient._has:Group:member:_tag", "http://example.org/tags|reviewed");

        var forwardChain = expression.ShouldBeOfType<ChainedExpression>();
        forwardChain.ResourceTypes.ShouldBe(ObservationResourceType);
        forwardChain.ReferenceSearchParameter.ShouldBeSameAs(observationPatient);
        forwardChain.TargetResourceTypes.ShouldBe(PatientResourceType);
        forwardChain.Reversed.ShouldBeFalse();

        var reverseChain = forwardChain.Expression.ShouldBeOfType<ChainedExpression>();
        reverseChain.ResourceTypes.ShouldBe(GroupResourceType);
        reverseChain.ReferenceSearchParameter.ShouldBeSameAs(groupMember);
        reverseChain.TargetResourceTypes.ShouldBe(PatientResourceType);
        reverseChain.Reversed.ShouldBeTrue();

        var terminal = reverseChain.Expression.ShouldBeOfType<SearchParameterExpression>();
        terminal.Parameter.ShouldBeSameAs(groupTag);
    }

    [Fact]
    public void GivenTokenSearchParameterWithNotModifierAndEscapedAlternatives_WhenParsingValue_ThenReturnsNotOrExpression()
    {
        var context = new SearchParserTestContext();
        var searchParameter = context.Add("Observation", "code", SearchParamType.Token);

        var expression = context.ValueParser.Parse(
            searchParameter,
            new SearchModifier(SearchModifierCode.Not),
            @"http://example.org|a\,b,http://example.org|c");

        var searchParameterExpression = expression.ShouldBeOfType<SearchParameterExpression>();
        searchParameterExpression.Parameter.ShouldBeSameAs(searchParameter);

        var notExpression = searchParameterExpression.Expression.ShouldBeOfType<NotExpression>();
        var orExpression = notExpression.Expression.ShouldBeOfType<MultiaryExpression>();
        orExpression.MultiaryOperation.ShouldBe(MultiaryOperator.Or);
        orExpression.Expressions.Count.ShouldBe(2);

        var firstAlternative = orExpression.Expressions[0].ShouldBeOfType<MultiaryExpression>();
        firstAlternative.MultiaryOperation.ShouldBe(MultiaryOperator.And);
        firstAlternative.Expressions.Count.ShouldBe(2);

        var firstSystem = firstAlternative.Expressions[0].ShouldBeOfType<StringExpression>();
        firstSystem.FieldName.ShouldBe(FieldName.TokenSystem);
        firstSystem.Value.ShouldBe("http://example.org");

        var firstCode = firstAlternative.Expressions[1].ShouldBeOfType<StringExpression>();
        firstCode.FieldName.ShouldBe(FieldName.TokenCode);
        firstCode.Value.ShouldBe("a,b");

        var secondAlternative = orExpression.Expressions[1].ShouldBeOfType<MultiaryExpression>();
        secondAlternative.MultiaryOperation.ShouldBe(MultiaryOperator.And);
        secondAlternative.Expressions.Count.ShouldBe(2);

        var secondSystem = secondAlternative.Expressions[0].ShouldBeOfType<StringExpression>();
        secondSystem.FieldName.ShouldBe(FieldName.TokenSystem);
        secondSystem.Value.ShouldBe("http://example.org");

        var secondCode = secondAlternative.Expressions[1].ShouldBeOfType<StringExpression>();
        secondCode.FieldName.ShouldBe(FieldName.TokenCode);
        secondCode.Value.ShouldBe("c");
    }

    [Theory]
    [InlineData("*:*", null, null)]
    [InlineData("Observation:*", "Observation", null)]
    [InlineData("Observation:subject", "Observation", "subject")]
    public void GivenNotReferencedQueries_WhenParsingValue_ThenReturnsExpectedMetadata(
        string value,
        string? expectedSourceResourceType,
        string? expectedReferencePath)
    {
        var context = new SearchParserTestContext();

        var expression = context.Parser.Parse(ObservationResourceType, "_not-referenced", value);

        var notReferenced = expression.ShouldBeOfType<NotReferencedExpression>();
        notReferenced.SourceResourceType.ShouldBe(expectedSourceResourceType);
        notReferenced.ReferencePath.ShouldBe(expectedReferencePath);
    }
}
