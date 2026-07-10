// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Binding;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class IncludeAndNotReferencedParserTests
{
    private static readonly string[] PatientResourceTypes = ["Patient"];
    private static readonly string[] ObservationSubjectTargets = ["Patient"];
    private static readonly string[] PatientReferenceTargets = ["Organization", "Practitioner"];
    private static readonly string[] PatientLinkTargets = ["Patient", "Organization"];

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
        var syntax = SearchKeyGrammar.ParseInclude(value);

        var include = syntax.ShouldBeOfType<IncludeKeySyntax>();
        include.SourceResourceType.ShouldBe(expectedSourceResourceType);
        include.SearchParameterName.ShouldBe(expectedSearchParameterName);
        include.TargetResourceType.ShouldBe(expectedTargetResourceType);
        include.Wildcard.ShouldBe(expectedWildcard);
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
        var syntax = SearchKeyGrammar.ParseNotReferenced(value);

        var notReferenced = syntax.ShouldBeOfType<NotReferencedKeySyntax>();
        notReferenced.SourceResourceType.ShouldBe(expectedSourceResourceType);
        notReferenced.ReferencePath.ShouldBe(expectedReferencePath);
    }

    [Theory]
    [InlineData("Observation:")]
    [InlineData("Observation:subject.name")]
    [InlineData("Observation:subject:extra")]
    [InlineData("Observation:subject name")]
    public void GivenMalformedNotReferencedSyntax_WhenParsing_ThenThrowsPositionedInvalidSearchOperation(string value)
    {
        var exception = Should.Throw<InvalidSearchOperationException>(() => SearchKeyGrammar.ParseNotReferenced(value));

        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column");
    }

    [Fact]
    public void GivenReverseIncludeSyntaxWithoutTargetAndNonIterate_WhenBinding_ThenDefaultsTargetToSearchResource()
    {
        var context = new SearchParserTestContext();
        var subject = context.Add("Observation", "subject", SearchParamType.Reference, targets: ObservationSubjectTargets);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = SearchKeyGrammar.ParseInclude("Observation:subject").ShouldBeOfType<IncludeKeySyntax>();

        var bound = binder.BindInclude(PatientResourceTypes, syntax, isReversed: true, iterate: false);

        var include = bound.ShouldBeOfType<BoundIncludeKey>();
        include.ReferenceSearchParameter.ShouldBeSameAs(subject);
        include.TargetResourceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenReverseIncludeSyntaxWithoutTargetAndIterate_WhenBinding_ThenDoesNotDefaultTarget()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: ObservationSubjectTargets);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = SearchKeyGrammar.ParseInclude("Observation:subject").ShouldBeOfType<IncludeKeySyntax>();

        var include = binder.BindInclude(PatientResourceTypes, syntax, isReversed: true, iterate: true);

        include.TargetResourceType.ShouldBeNull();
    }

    [Fact]
    public void GivenWildcardIncludeSyntax_WhenBinding_ThenReturnsDistinctReferencedTypesInDefinitionOrder()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "general-practitioner", SearchParamType.Reference, targets: PatientReferenceTargets);
        context.Add("Patient", "link", SearchParamType.Reference, targets: PatientLinkTargets);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = SearchKeyGrammar.ParseInclude("Patient:*").ShouldBeOfType<IncludeKeySyntax>();

        var include = binder.BindInclude(PatientResourceTypes, syntax, isReversed: false, iterate: false);

        include.ReferenceSearchParameter.ShouldBeNull();
        include.ReferencedTypes.ShouldBe(["Organization", "Practitioner", "Patient"], ignoreOrder: false);
    }

    [Fact]
    public void GivenDomainResourceInclude_WhenBinding_ThenThrowsInvalidSearchOperation()
    {
        var context = new SearchParserTestContext();
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = SearchKeyGrammar.ParseInclude("Observation:*").ShouldBeOfType<IncludeKeySyntax>();

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => binder.BindInclude(["DomainResource"], syntax, isReversed: false, iterate: false));

        exception.Message.ShouldContain("base route");
    }

    [Fact]
    public void GivenIncludeWithInvalidExplicitTarget_WhenBinding_ThenThrowsInvalidSearchOperation()
    {
        var context = new SearchParserTestContext();
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = SearchKeyGrammar.ParseInclude("Observation:subject:FakeType").ShouldBeOfType<IncludeKeySyntax>();

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => binder.BindInclude(PatientResourceTypes, syntax, isReversed: false, iterate: false));

        exception.Message.ShouldContain("FakeType");
    }

    [Fact]
    public void GivenNotReferencedWithInvalidSource_WhenBinding_ThenThrowsInvalidSearchOperation()
    {
        var context = new SearchParserTestContext();
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = SearchKeyGrammar.ParseNotReferenced("FakeType:subject").ShouldBeOfType<NotReferencedKeySyntax>();

        var exception = Should.Throw<InvalidSearchOperationException>(() => binder.BindNotReferenced(syntax));

        exception.Message.ShouldBe("Invalid resource type in _not-referenced: 'FakeType'");
    }

    [Fact]
    public void GivenIncludeWithTrailingTargetColon_WhenParsing_ThenThrowsPositionedInvalidSearchOperation()
    {
        var exception = Should.Throw<InvalidSearchOperationException>(() => SearchKeyGrammar.ParseInclude("Observation:subject:"));

        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column");
    }
}
