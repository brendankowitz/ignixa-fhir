// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Binding;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Search;
using Ignixa.Specification.ValueSets.Normative;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchKeyBinderTests
{
    private static readonly string[] ObservationResourceTypes = ["Observation"];
    private static readonly string[] PatientResourceTypes = ["Patient"];
    private static readonly string[] GroupResourceTypes = ["Group"];
    private static readonly string[] PatientAndPractitionerResourceTypes = ["Patient", "Practitioner"];
    private static readonly string[] SubjectTargets = ["Patient", "Group"];

    [Fact]
    public void GivenTypedForwardChain_WhenBinding_ThenReturnsBoundChainWithTypedTarget()
    {
        var context = new SearchParserTestContext();
        var subject = context.Add("Observation", "subject", SearchParamType.Reference, targets: SubjectTargets);
        var patientName = context.Add("Patient", "name", SearchParamType.String);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new ForwardChainKeySyntax("subject", "Patient", new ParameterKeySyntax("name", null));

        var bound = binder.Bind(ObservationResourceTypes, syntax);

        var chain = bound.ShouldBeOfType<BoundChainKey>();
        chain.ReferenceSearchParameter.ShouldBeSameAs(subject);
        chain.ResourceTypes.ShouldBe(ObservationResourceTypes);
        chain.TargetResourceTypes.ShouldBe(PatientResourceTypes);
        chain.Reversed.ShouldBeFalse();

        var next = chain.Next.ShouldBeOfType<BoundParameterKey>();
        next.SearchParameter.ShouldBeSameAs(patientName);
        next.Modifier.ShouldBeNull();
    }

    [Fact]
    public void GivenTypedForwardChainWithUnsupportedTarget_WhenBinding_ThenThrowsChainedParameterNotSupported()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: SubjectTargets);
        context.DefinitionManager.GetSearchParameter("Patient", "name")
            .Returns(_ => throw new SearchParameterNotSupportedException("Patient", "name"));
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new ForwardChainKeySyntax("subject", "Patient", new ParameterKeySyntax("name", null));

        var exception = Should.Throw<InvalidSearchOperationException>(() => binder.Bind(ObservationResourceTypes, syntax));

        exception.Message.ShouldBe(Resources.ChainedParameterNotSupported);
    }

    [Fact]
    public void GivenNonReferenceForwardChain_WhenBinding_ThenThrowsInvalidSearchOperation()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "code", SearchParamType.Token);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new ForwardChainKeySyntax("code", null, new ParameterKeySyntax("name", null));

        var exception = Should.Throw<InvalidSearchOperationException>(() => binder.Bind(ObservationResourceTypes, syntax));

        exception.Message.ShouldBe(Resources.ChainedParameterMustBeReferenceSearchParamType);
    }

    [Fact]
    public void GivenUntypedForwardChainWithMultipleSupportedTargets_WhenBinding_ThenThrowsSpecifyType()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: SubjectTargets);
        context.Add("Patient", "name", SearchParamType.String);
        context.Add("Group", "name", SearchParamType.String);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new ForwardChainKeySyntax("subject", null, new ParameterKeySyntax("name", null));

        var exception = Should.Throw<InvalidSearchOperationException>(() => binder.Bind(ObservationResourceTypes, syntax));

        exception.Message.ShouldContain("subject:Patient");
        exception.Message.ShouldContain("subject:Group");
    }

    [Fact]
    public void GivenUntypedForwardChainWithUnsupportedTarget_WhenBinding_ThenSkipsUnsupportedTarget()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: SubjectTargets);
        var patientName = context.Add("Patient", "name", SearchParamType.String);
        context.DefinitionManager.GetSearchParameter("Group", "name")
            .Returns(_ => throw new SearchParameterNotSupportedException("Group", "name"));
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new ForwardChainKeySyntax("subject", null, new ParameterKeySyntax("name", null));

        var bound = binder.Bind(ObservationResourceTypes, syntax);

        var chain = bound.ShouldBeOfType<BoundChainKey>();
        chain.TargetResourceTypes.ShouldBe(PatientResourceTypes);
        var next = chain.Next.ShouldBeOfType<BoundParameterKey>();
        next.SearchParameter.ShouldBeSameAs(patientName);
    }

    [Fact]
    public void GivenReverseChain_WhenBinding_ThenResolvesNextInSourceResourceContext()
    {
        var context = new SearchParserTestContext();
        var member = context.Add("Group", "member", SearchParamType.Reference, targets: PatientResourceTypes);
        var groupTag = context.Add("Group", "_tag", SearchParamType.Token);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new ReverseChainKeySyntax(
            "Group",
            "member",
            new ParameterKeySyntax("_tag", null));

        var bound = binder.Bind(PatientResourceTypes, syntax);

        var chain = bound.ShouldBeOfType<BoundChainKey>();
        chain.ResourceTypes.ShouldBe(GroupResourceTypes);
        chain.ReferenceSearchParameter.ShouldBeSameAs(member);
        chain.TargetResourceTypes.ShouldBe(PatientResourceTypes);
        chain.Reversed.ShouldBeTrue();
        var next = chain.Next.ShouldBeOfType<BoundParameterKey>();
        next.SearchParameter.ShouldBeSameAs(groupTag);
        next.Modifier.ShouldBeNull();
    }

    [Fact]
    public void GivenDifferentDefinitionContexts_WhenBindingSameSyntax_ThenUsesContextSpecificParameterInstances()
    {
        var contextA = new SearchParserTestContext();
        var contextB = new SearchParserTestContext();
        var contextAName = contextA.Add("Patient", "name", SearchParamType.String);
        var contextBName = contextB.Add("Patient", "name", SearchParamType.Token);
        var syntax = new ParameterKeySyntax("name", null);
        var binderA = new SearchKeyBinder(contextA.DefinitionManager, contextA.SchemaProvider);
        var binderB = new SearchKeyBinder(contextB.DefinitionManager, contextB.SchemaProvider);

        var boundA = binderA.Bind(PatientResourceTypes, syntax).ShouldBeOfType<BoundParameterKey>();
        var boundB = binderB.Bind(PatientResourceTypes, syntax).ShouldBeOfType<BoundParameterKey>();

        boundA.SearchParameter.ShouldBeSameAs(contextAName);
        boundB.SearchParameter.ShouldBeSameAs(contextBName);
        ReferenceEquals(boundA.SearchParameter, boundB.SearchParameter).ShouldBeFalse();
    }

    [Fact]
    public void GivenParameterNotCommonAcrossResourceTypes_WhenBinding_ThenThrowsBadSearchRequest()
    {
        var context = new SearchParserTestContext();
        var patientName = context.Add("Patient", "name", SearchParamType.String);
        var practitionerName = context.Add("Practitioner", "name", SearchParamType.String);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new ParameterKeySyntax("name", null);

        ReferenceEquals(patientName, practitionerName).ShouldBeFalse();
        var exception = Should.Throw<BadSearchRequestException>(() => binder.Bind(PatientAndPractitionerResourceTypes, syntax));

        exception.Message.ShouldContain("not common");
    }

    [Fact]
    public void GivenLiteralTypeModifierOnReferenceParameter_WhenBinding_ThenThrowsInvalidSearchOperation()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: PatientResourceTypes);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new ParameterKeySyntax("subject", "type");

        Should.Throw<InvalidSearchOperationException>(() => binder.Bind(ObservationResourceTypes, syntax));
    }

    [Fact]
    public void GivenStandardModifier_WhenBindingParameter_ThenBindsSearchModifierCode()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new ParameterKeySyntax("name", "exact");

        var bound = binder.Bind(PatientResourceTypes, syntax).ShouldBeOfType<BoundParameterKey>();

        bound.Modifier.ShouldNotBeNull();
        bound.Modifier.SearchModifierCode.ShouldBe(SearchModifierCode.Exact);
    }

    [Fact]
    public void GivenReferenceTargetModifier_WhenBindingParameter_ThenBindsTypeModifierWithTarget()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, targets: PatientResourceTypes);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new ParameterKeySyntax("subject", "Patient");

        var bound = binder.Bind(ObservationResourceTypes, syntax).ShouldBeOfType<BoundParameterKey>();

        bound.Modifier.ShouldNotBeNull();
        bound.Modifier.SearchModifierCode.ShouldBe(SearchModifierCode.Type);
        bound.Modifier.ResourceType.ShouldBe("Patient");
    }
}
