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

    [Fact]
    public void GivenReverseIncludeSyntaxWithoutTargetAndNonIterate_WhenBinding_ThenDefaultsTargetToSearchResource()
    {
        var context = new SearchParserTestContext();
        var subject = context.Add("Observation", "subject", SearchParamType.Reference, targets: ObservationSubjectTargets);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new IncludeKeySyntax("Observation", "subject", null, false);

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
        var syntax = new IncludeKeySyntax("Observation", "subject", null, false);

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
        var syntax = new IncludeKeySyntax("Patient", null, null, true);

        var include = binder.BindInclude(PatientResourceTypes, syntax, isReversed: false, iterate: false);

        include.ReferenceSearchParameter.ShouldBeNull();
        include.ReferencedTypes.ShouldBe(["Organization", "Practitioner", "Patient"], ignoreOrder: false);
    }

    [Fact]
    public void GivenDomainResourceInclude_WhenBinding_ThenThrowsInvalidSearchOperation()
    {
        var context = new SearchParserTestContext();
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new IncludeKeySyntax("Observation", null, null, true);

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => binder.BindInclude(["DomainResource"], syntax, isReversed: false, iterate: false));

        exception.Message.ShouldContain("base route");
    }

    [Fact]
    public void GivenIncludeWithInvalidExplicitTarget_WhenBinding_ThenThrowsInvalidSearchOperation()
    {
        var context = new SearchParserTestContext();
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new IncludeKeySyntax("Observation", "subject", "FakeType", false);

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => binder.BindInclude(PatientResourceTypes, syntax, isReversed: false, iterate: false));

        exception.Message.ShouldContain("FakeType");
    }

    [Fact]
    public void GivenNotReferencedWithInvalidSource_WhenBinding_ThenThrowsInvalidSearchOperation()
    {
        var context = new SearchParserTestContext();
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new NotReferencedKeySyntax("FakeType", "subject");

        var exception = Should.Throw<InvalidSearchOperationException>(() => binder.BindNotReferenced(syntax));

        exception.Message.ShouldBe("Invalid resource type in _not-referenced: 'FakeType'");
    }
}
