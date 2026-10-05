using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Specification.Extensions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Shouldly;

namespace Ignixa.Validation.Tests.Schema;

public class GeneratedContentReferenceSchemaTests
{
    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public void GivenValueSetWithoutCompose_WhenBuildingAndValidating_ThenNoContentReferenceFailure(FhirVersion version)
    {
        var provider = version.GetSchemaProvider();

        var schema = new StructureDefinitionSchemaResolver(provider)
            .GetSchema("http://hl7.org/fhir/StructureDefinition/ValueSet");
        schema.ShouldNotBeNull();
        var result = schema.Validate(JsonSourceNodeFactory.Parse("""
            {"resourceType":"ValueSet","status":"active","url":"https://example.org/ValueSet/test","name":"Test",
             "meta":{"tag":[{"system":"https://example.org","code":"uri-test"}]}}
            """).ToElement(provider), new ValidationSettings { Depth = ValidationDepth.Spec });

        result.IsValid.ShouldBeTrue(string.Join("; ", result.Issues.Select(i => i.Message)));
    }

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public void GivenValueSetExcludeWithMissingConceptCode_WhenValidating_ThenAppliesReferencedIncludeSchema(FhirVersion version)
    {
        var provider = version.GetSchemaProvider();
        var schema = new StructureDefinitionSchemaResolver(provider)
            .GetSchema("http://hl7.org/fhir/StructureDefinition/ValueSet")!;

        var result = schema.Validate(JsonSourceNodeFactory.Parse("""
            {"resourceType":"ValueSet","status":"active","compose":{
             "include":[{"system":"https://example.org","concept":[{"code":"valid"}]}],
             "exclude":[{"system":"https://example.org","concept":[{"display":"Missing code"}]}]}}
            """).ToElement(provider), new ValidationSettings { Depth = ValidationDepth.Spec });

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Path.Contains("exclude", StringComparison.Ordinal)
            && i.Path.Contains("code", StringComparison.Ordinal));
    }
}
