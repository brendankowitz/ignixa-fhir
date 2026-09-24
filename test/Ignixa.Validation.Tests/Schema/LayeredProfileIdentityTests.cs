using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Ignixa.Serialization;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Shouldly;

namespace Ignixa.Validation.Tests.Schema;

public class LayeredProfileIdentityTests
{
    [Fact]
    public void GivenSameIdUnderDifferentCanonicals_WhenResolving_ThenUsesFullIdentity()
    {
        var provider = new ProfileLayeredSchemaProvider(new R4CoreSchemaProvider(),
        [
            Profile("https://first.example/review-patient", "review-patient", "1", "active", "boolean"),
            Profile("https://second.example/review-patient", "review-patient", "1", "gender", "code")
        ]);
        var resolver = new StructureDefinitionSchemaResolver(provider);
        var first = resolver.GetSchema("https://first.example/review-patient");
        var second = resolver.GetSchema("https://second.example/review-patient");
        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        var element = JsonSourceNodeFactory.Parse("""{"resourceType":"Patient","active":true}""").ToElement(provider);
        first.Validate(element, new ValidationSettings { Depth = ValidationDepth.Minimal }).IsValid.ShouldBeTrue();
        second.Validate(element, new ValidationSettings { Depth = ValidationDepth.Minimal }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void GivenSameCanonicalWithTwoBusinessVersions_WhenResolving_ThenKeepsVersionsSeparate()
    {
        const string canonical = "https://example.org/review-patient";
        var provider = new ProfileLayeredSchemaProvider(new R4CoreSchemaProvider(),
        [
            Profile(canonical, "review-patient", "1", "active", "boolean"),
            Profile(canonical, "review-patient", "2", "gender", "code")
        ]);
        var resolver = new StructureDefinitionSchemaResolver(provider);
        var first = resolver.GetSchema(canonical + "|1");
        var second = resolver.GetSchema(canonical + "|2");
        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        var element = JsonSourceNodeFactory.Parse("""{"resourceType":"Patient","active":true}""").ToElement(provider);
        first.Validate(element, new ValidationSettings { Depth = ValidationDepth.Minimal }).IsValid.ShouldBeTrue();
        second.Validate(element, new ValidationSettings { Depth = ValidationDepth.Minimal }).IsValid.ShouldBeFalse();
        resolver.GetSchema(canonical + "|missing").ShouldBeNull();
    }

    [Fact]
    public void GivenProfileIdMatchingUnavailableCoreName_WhenResolvingCoreCanonical_ThenDoesNotUseTheProfileAlias()
    {
        const string canonical = "https://example.org/review-patient";
        var provider = new ProfileLayeredSchemaProvider(new R4CoreSchemaProvider(),
            [Profile(canonical, "review-patient", "1", "active", "boolean")]);
        var resolver = new StructureDefinitionSchemaResolver(provider);

        resolver.GetSchema("http://hl7.org/fhir/StructureDefinition/review-patient").ShouldBeNull();
        resolver.GetSchema("http://hl7.org/fhir/StructureDefinition/review-patient|4.0.1").ShouldBeNull();
        resolver.GetSchema(canonical + "|1").ShouldNotBeNull();
        provider.GetTypeDefinition("review-patient").ShouldBeSameAs(provider.GetTypeDefinition(canonical));
    }

    [Fact]
    public void GivenProfileIdMatchingCoreType_WhenResolvingCoreCanonical_ThenDoesNotValidateAnUnrelatedProfile()
    {
        var provider = new ProfileLayeredSchemaProvider(new R4CoreSchemaProvider(),
            [Profile("https://example.org/custom-patient", "Patient", "1", "active", "boolean")]);
        var resolver = new StructureDefinitionSchemaResolver(provider);
        var element = JsonSourceNodeFactory.Parse("""{"resourceType":"Patient"}""").ToElement(new R4CoreSchemaProvider());
        resolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient")!
            .Validate(element, new ValidationSettings { Depth = ValidationDepth.Minimal }).IsValid.ShouldBeTrue();
        resolver.GetSchema("https://example.org/custom-patient")!
            .Validate(element, new ValidationSettings { Depth = ValidationDepth.Minimal }).IsValid.ShouldBeFalse();
        provider.GetTypeDefinition("Patient").ShouldBeSameAs(provider.GetTypeDefinition("https://example.org/custom-patient"));
    }

    private static ExtractedResource Profile(string canonical, string id, string version, string field, string type) => new()
    {
        ResourceType = "StructureDefinition", Canonical = canonical, ResourceId = id, Version = version, FhirVersion = "4.0.1",
        ResourceJson = $$$"""
            {"resourceType":"StructureDefinition","id":"{{{id}}}","url":"{{{canonical}}}","version":"{{{version}}}",
            "type":"Patient","kind":"resource","snapshot":{"element":[
              {"path":"Patient","min":0,"max":"*"},
              {"path":"Patient.{{{field}}}","min":1,"max":"1","type":[{"code":"{{{type}}}"}]}
            ]}}
            """
    };
}
