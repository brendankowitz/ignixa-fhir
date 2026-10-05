using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Ignixa.Serialization;
using Ignixa.Specification.Extensions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Shouldly;

namespace Ignixa.Validation.Tests.Schema;

public class GeneratedAbsoluteContentReferenceTests
{
    private const string CoreCanonical = "http://hl7.org/fhir/StructureDefinition/ExplanationOfBenefit";
    private const string Fragment = "ExplanationOfBenefit.item.adjudication";
    private const string CallerCanonical = "https://example.org/StructureDefinition/AdjudicationModel";

    [Theory]
    [InlineData(FhirVersion.R4, false)]
    [InlineData(FhirVersion.R4, true)]
    [InlineData(FhirVersion.R4B, false)]
    [InlineData(FhirVersion.R4B, true)]
    [InlineData(FhirVersion.R5, false)]
    [InlineData(FhirVersion.R5, true)]
    public void GivenAbsoluteCoreReference_WhenBuildingAndValidating_ThenUsesActualGeneratedBackbone(
        FhirVersion version, bool versioned)
    {
        var core = version.GetSchemaProvider();
        string reference = CoreCanonical + (versioned ? "|" + core.FullVersion : string.Empty) + "#" + Fragment;
        var provider = new ProfileLayeredSchemaProvider(core, [Caller(reference)]);

        var schema = new StructureDefinitionSchemaResolver(provider).GetSchema(CallerCanonical);

        schema.ShouldNotBeNull();
        var valid = Validate(schema, provider, """{"category":{"text":"covered"}}""");
        valid.IsValid.ShouldBeTrue(string.Join("; ", valid.Issues.Select(i => i.Message)));
        var invalid = Validate(schema, provider, """{"reason":{"text":"missing category"}}""");
        invalid.IsValid.ShouldBeFalse();
        invalid.Issues.ShouldContain(i => i.Path.Contains("category", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://other.example/StructureDefinition/ExplanationOfBenefit#ExplanationOfBenefit.item.adjudication")]
    [InlineData(CoreCanonical + "|99.0.0#" + Fragment)]
    [InlineData("http://hl7.org/fhir/StructureDefinition/Patient#ExplanationOfBenefit.item.adjudication")]
    [InlineData(CoreCanonical + "#ExplanationOfBenefit.nonexistent")]
    public void GivenUnavailableOrMismatchedCanonicalReference_WhenBuilding_ThenDoesNotSubstituteCoreByTail(string reference)
    {
        var provider = new ProfileLayeredSchemaProvider(FhirVersion.R4.GetSchemaProvider(), [Caller(reference)]);

        Should.Throw<InvalidOperationException>(() => new StructureDefinitionSchemaResolver(provider).GetSchema(CallerCanonical));
    }

    [Fact]
    public void GivenLegacyAliasMatchingCoreBackbone_WhenResolvingAbsoluteReference_ThenIgnoresUnrelatedProfileAlias()
    {
        const string aliasCanonical = "https://unrelated.example/StructureDefinition/backbone";
        var alias = new ExtractedResource
        {
            ResourceType = "StructureDefinition", ResourceId = Fragment, Canonical = aliasCanonical, FhirVersion = "4.0.1",
            ResourceJson = $$$"""
                {"resourceType":"StructureDefinition","url":"{{{aliasCanonical}}}","type":"Patient","kind":"resource",
                 "snapshot":{"element":[{"path":"Patient"},{"path":"Patient.active","min":1,"max":"1","type":[{"code":"boolean"}]}]}}
                """
        };
        var provider = new ProfileLayeredSchemaProvider(FhirVersion.R4.GetSchemaProvider(),
            [Caller(CoreCanonical + "#" + Fragment), alias]);
        provider.GetTypeDefinition(Fragment)!.Info.Name.ShouldBe("Patient");

        var schema = new StructureDefinitionSchemaResolver(provider).GetSchema(CallerCanonical)!;
        var result = Validate(schema, provider, """{"category":{"text":"covered"}}""");

        result.IsValid.ShouldBeTrue(string.Join("; ", result.Issues.Select(i => i.Message)));
    }

    private static ValidationResult Validate(ValidationSchema schema, ISchema provider, string adjudication)
        => schema.Validate(JsonSourceNodeFactory.Parse($$"""
            {"resourceType":"AdjudicationModel","adjudication":[{{adjudication}}]}
            """).ToElement(provider), new ValidationSettings { Depth = ValidationDepth.Spec });

    private static ExtractedResource Caller(string reference) => new()
    {
        ResourceType = "StructureDefinition", ResourceId = "AdjudicationModel",
        Canonical = CallerCanonical, FhirVersion = "4.0.1",
        ResourceJson = $$$"""
            {"resourceType":"StructureDefinition","url":"{{{CallerCanonical}}}","type":"{{{CallerCanonical}}}",
             "kind":"logical","abstract":false,"derivation":"specialization",
             "snapshot":{"element":[
               {"path":"AdjudicationModel","min":0,"max":"1"},
               {"path":"AdjudicationModel.adjudication","min":0,"max":"*","contentReference":"{{{reference}}}"}
             ]}}
            """
    };
}
