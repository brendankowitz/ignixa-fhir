// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Schema;

/// <summary>
/// Tests for <see cref="ProfileAwareValidationSchemaResolver"/>: walks
/// <c>Resource.meta.profile</c>, resolves each URL via the inner resolver, and composes
/// the resulting schemas into a single schema whose checks are the union.
/// </summary>
public class ProfileAwareValidationSchemaResolverTests
{
    private static IElement ParseElement(string json)
    {
        var node = JsonNode.Parse(json);
        node.ShouldNotBeNull();
        return JsonNodeSourceNode.Create(node!).ToElement(TestHelpers.TestSchemaProvider.GetR4Schema());
    }

    private static ValidationSchema MakeEmptySchema(string canonicalUrl, string resourceType)
        => new(canonicalUrl, resourceType,
            universalChecks: Array.Empty<IValidationCheck>(),
            specChecks: Array.Empty<IValidationCheck>(),
            profileChecks: Array.Empty<IValidationCheck>());

    private static ValidationSchema MakeSchemaWithProfileCheck(string canonicalUrl, string resourceType, IValidationCheck check)
        => new(canonicalUrl, resourceType,
            universalChecks: Array.Empty<IValidationCheck>(),
            specChecks: Array.Empty<IValidationCheck>(),
            profileChecks: new[] { check });

    [Fact]
    public void GivenResourceWithoutMetaProfile_WhenResolving_ThenReturnsOnlyBaseSchema()
    {
        var inner = Substitute.For<IValidationSchemaResolver>();
        var baseSchema = MakeEmptySchema("http://hl7.org/fhir/StructureDefinition/Patient", "Patient");
        inner.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient").Returns(baseSchema);

        var resolver = new ProfileAwareValidationSchemaResolver(inner);
        var element = ParseElement("{\"resourceType\":\"Patient\",\"id\":\"x\"}");

        var resolved = resolver.ResolveForElement(element);

        resolved.ShouldNotBeNull();
        resolved!.ResourceType.ShouldBe("Patient");
        inner.Received(1).GetSchema("http://hl7.org/fhir/StructureDefinition/Patient");
    }

    [Fact]
    public void GivenResourceWithMetaProfile_WhenResolving_ThenComposesBaseAndProfileChecks()
    {
        var inner = Substitute.For<IValidationSchemaResolver>();
        var baseCheck = Substitute.For<IValidationCheck>();
        var profileCheck = Substitute.For<IValidationCheck>();
        inner.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient")
            .Returns(MakeSchemaWithProfileCheck("http://hl7.org/fhir/StructureDefinition/Patient", "Patient", baseCheck));
        inner.GetSchema("http://example.org/StructureDefinition/MyPatient")
            .Returns(MakeSchemaWithProfileCheck("http://example.org/StructureDefinition/MyPatient", "Patient", profileCheck));

        var resolver = new ProfileAwareValidationSchemaResolver(inner);
        var element = ParseElement("""
            {"resourceType":"Patient","id":"x","meta":{"profile":["http://example.org/StructureDefinition/MyPatient"]}}
            """);

        var resolved = resolver.ResolveForElement(element);

        resolved.ShouldNotBeNull();
        resolved!.Checks.Count.ShouldBe(2);
        resolved.Checks.ShouldContain(baseCheck);
        resolved.Checks.ShouldContain(profileCheck);
    }

    [Fact]
    public void GivenMetaProfileWithVersionSuffix_WhenResolving_ThenStripsVersionForLookup()
    {
        var inner = Substitute.For<IValidationSchemaResolver>();
        inner.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient")
            .Returns(MakeEmptySchema("http://hl7.org/fhir/StructureDefinition/Patient", "Patient"));
        var profileSchema = MakeEmptySchema("http://example.org/StructureDefinition/MyPatient", "Patient");
        inner.GetSchema("http://example.org/StructureDefinition/MyPatient")
            .Returns(profileSchema);

        var resolver = new ProfileAwareValidationSchemaResolver(inner);
        var element = ParseElement("""
            {"resourceType":"Patient","id":"x","meta":{"profile":["http://example.org/StructureDefinition/MyPatient|2.1.0"]}}
            """);

        var resolved = resolver.ResolveForElement(element);

        resolved.ShouldNotBeNull();
        // The inner resolver must have been queried with the unversioned canonical.
        inner.Received(1).GetSchema("http://example.org/StructureDefinition/MyPatient");
    }

    [Fact]
    public void GivenMetaProfileWithUnresolvableProfile_WhenResolving_ThenSkipsProfileAndReturnsBase()
    {
        var inner = Substitute.For<IValidationSchemaResolver>();
        var baseSchema = MakeEmptySchema("http://hl7.org/fhir/StructureDefinition/Patient", "Patient");
        inner.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient").Returns(baseSchema);
        inner.GetSchema("http://example.org/StructureDefinition/MissingProfile").Returns((ValidationSchema?)null);

        var resolver = new ProfileAwareValidationSchemaResolver(inner);
        var element = ParseElement("""
            {"resourceType":"Patient","id":"x","meta":{"profile":["http://example.org/StructureDefinition/MissingProfile"]}}
            """);

        var resolved = resolver.ResolveForElement(element);

        resolved.ShouldNotBeNull();
        resolved!.ResourceType.ShouldBe("Patient");
        // Profile that didn't resolve must not produce a null reference / crash.
    }

    [Fact]
    public void GivenResourceWithoutResourceType_WhenResolving_ThenReturnsNull()
    {
        var inner = Substitute.For<IValidationSchemaResolver>();
        var resolver = new ProfileAwareValidationSchemaResolver(inner);
        var element = ParseElement("{\"id\":\"x\"}");

        var resolved = resolver.ResolveForElement(element);

        resolved.ShouldBeNull();
    }

    [Fact]
    public void GivenMultipleProfiles_WhenResolving_ThenAllProfileChecksMerged()
    {
        var inner = Substitute.For<IValidationSchemaResolver>();
        var c1 = Substitute.For<IValidationCheck>();
        var c2 = Substitute.For<IValidationCheck>();
        inner.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient")
            .Returns(MakeEmptySchema("http://hl7.org/fhir/StructureDefinition/Patient", "Patient"));
        inner.GetSchema("http://example.org/p1")
            .Returns(MakeSchemaWithProfileCheck("http://example.org/p1", "Patient", c1));
        inner.GetSchema("http://example.org/p2")
            .Returns(MakeSchemaWithProfileCheck("http://example.org/p2", "Patient", c2));

        var resolver = new ProfileAwareValidationSchemaResolver(inner);
        var element = ParseElement("""
            {"resourceType":"Patient","id":"x","meta":{"profile":["http://example.org/p1","http://example.org/p2"]}}
            """);

        var resolved = resolver.ResolveForElement(element);

        resolved.ShouldNotBeNull();
        resolved!.Checks.ShouldContain(c1);
        resolved.Checks.ShouldContain(c2);
    }

    [Fact]
    public void GivenDuplicateProfilesInMetaProfile_WhenResolving_ThenProfileSchemaComposedOnce()
    {
        // FHIR doesn't forbid duplicate URLs in meta.profile. Without dedup the same
        // profile schema would compose twice, producing every profile-derived check
        // twice and pretending we ran extra validation.
        var inner = Substitute.For<IValidationSchemaResolver>();
        var profileCheck = Substitute.For<IValidationCheck>();
        inner.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient")
            .Returns(MakeEmptySchema("http://hl7.org/fhir/StructureDefinition/Patient", "Patient"));
        inner.GetSchema("http://example.org/profile")
            .Returns(MakeSchemaWithProfileCheck("http://example.org/profile", "Patient", profileCheck));

        var resolver = new ProfileAwareValidationSchemaResolver(inner);
        var element = ParseElement("""
            {"resourceType":"Patient","id":"x","meta":{"profile":["http://example.org/profile","http://example.org/profile|2.0.0"]}}
            """);

        var resolved = resolver.ResolveForElement(element);

        resolved.ShouldNotBeNull();
        // Inner resolver should have been queried for the profile exactly once,
        // even though meta.profile listed it twice (with and without version suffix).
        inner.Received(1).GetSchema("http://example.org/profile");
        // Composed schema should contain the profile's check exactly once.
        resolved!.Checks.Count(c => ReferenceEquals(c, profileCheck)).ShouldBe(1);
    }

    // ===== IValidationSchemaResolver interface contract (drop-in replacement) =====

    [Fact]
    public void GivenResolverUsedAsIValidationSchemaResolver_WhenCallingGetSchema_ThenDelegatesToInner()
    {
        // Production DI registers this class behind the IValidationSchemaResolver interface.
        // Legacy callers that only know about GetSchema(canonicalUrl) must keep working.
        var inner = Substitute.For<IValidationSchemaResolver>();
        var expectedSchema = MakeEmptySchema("http://hl7.org/fhir/StructureDefinition/Patient", "Patient");
        inner.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient").Returns(expectedSchema);

        IValidationSchemaResolver wrapper = new ProfileAwareValidationSchemaResolver(inner);

        var result = wrapper.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient");

        result.ShouldBe(expectedSchema);
        inner.Received(1).GetSchema("http://hl7.org/fhir/StructureDefinition/Patient");
    }

    [Fact]
    public void GivenResolverUsedAsIValidationSchemaResolver_WhenInnerReturnsNull_ThenWrapperReturnsNull()
    {
        var inner = Substitute.For<IValidationSchemaResolver>();
        inner.GetSchema(Arg.Any<string>()).Returns((ValidationSchema?)null);

        IValidationSchemaResolver wrapper = new ProfileAwareValidationSchemaResolver(inner);

        wrapper.GetSchema("http://example.org/missing").ShouldBeNull();
    }

    [Fact]
    public void GivenResourceWithUnresolvableProfile_WhenResolveForElement_ThenComposedSchemaContainsWarningCheck()
    {
        var inner = Substitute.For<IValidationSchemaResolver>();
        var baseSchema = new ValidationSchema(
            "http://hl7.org/fhir/StructureDefinition/Patient",
            "Patient",
            universalChecks: [],
            specChecks: [],
            profileChecks: []);
        inner.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient").Returns(baseSchema);
        inner.GetSchema(Arg.Is<string>(s => s.Contains("us-core"))).Returns((ValidationSchema?)null);

        var resolver = new ProfileAwareValidationSchemaResolver(inner);

        var json = """{"resourceType":"Patient","id":"p1","meta":{"profile":["http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"]}}""";
        var element = ParseElement(json);

        var schema = resolver.ResolveForElement(element);

        schema.ShouldNotBeNull();

        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var state = new ValidationState();
        var result = schema!.Validate(element, settings, state);

        result.Issues.ShouldContain(
            i => i.Severity == IssueSeverity.Warning && i.Message!.Contains("us-core-patient"),
            "Expected a warning issue for the unresolvable profile");
    }
}
