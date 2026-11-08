// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
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
    public void GivenWildcardIncludeSyntax_WhenBinding_ThenReturnsOnlySearchableReferenceParameters()
    {
        var context = new SearchParserTestContext();
        var generalPractitioner = context.Add("Patient", "general-practitioner", SearchParamType.Reference, targets: PatientReferenceTargets);
        context.Add("Patient", "link", SearchParamType.Reference, targets: PatientLinkTargets).IsSearchable = false;
        context.Add("Patient", "managing-organization", SearchParamType.Reference, targets: ["Organization"]).IsHiddenByTransition = true;
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = new IncludeKeySyntax("Patient", null, null, true);

        var include = binder.BindInclude(PatientResourceTypes, syntax, isReversed: false, iterate: false);

        include.ReferenceSearchParameter.ShouldBeNull();
        include.WildcardReferenceSearchParameters.ShouldBe([new WildcardIncludeParameter("Patient", generalPractitioner)]);
        include.ReferencedTypes.ShouldBe(["Organization", "Practitioner"], ignoreOrder: false);
    }

    // A parameter hidden on one type can share its storage identity with one searchable on another; a reverse
    // wildcard must not follow the hidden type's references because the other type's are searchable.
    [Fact]
    public void GivenAReferenceHiddenOnOneTypeOnly_WhenBindingAReverseWildcard_ThenOnlyTheSearchableTypeIsFollowed()
    {
        var context = new SearchParserTestContext();
        var shared = new Uri("http://hl7.org/fhir/SearchParameter/clinical-patient");
        var condition = context.Add("Condition", "patient", SearchParamType.Reference, targets: ["Patient"], url: shared);
        context.Add("AllergyIntolerance", "patient", SearchParamType.Reference, targets: ["Patient"], url: shared).IsHiddenByTransition = true;
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);

        var include = binder.BindInclude(PatientResourceTypes, new IncludeKeySyntax("*", null, null, true), isReversed: true, iterate: false);

        include.WildcardReferenceSearchParameters.ShouldBe([new WildcardIncludeParameter("Condition", condition)]);
    }

    // An iterative wildcard is seeded by included resources, so it follows the references of their types too.
    [Fact]
    public void GivenAnIterativeWildcardInclude_WhenBinding_ThenReferencesOfTypesOutsideTheSearchAreFollowed()
    {
        var context = new SearchParserTestContext();
        var organization = context.Add("Patient", "organization", SearchParamType.Reference, targets: ["Organization"]);
        var partOf = context.Add("Organization", "partof", SearchParamType.Reference, targets: ["Organization"]);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);

        var include = binder.BindInclude(PatientResourceTypes, new IncludeKeySyntax("*", null, null, true), isReversed: false, iterate: true);

        include.WildcardReferenceSearchParameters.ShouldBe(
            [new WildcardIncludeParameter("Patient", organization), new WildcardIncludeParameter("Organization", partOf)],
            ignoreOrder: true);
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
