using Ignixa.Specification.Generated;
using Ignixa.Validation.Schema;
using Shouldly;

namespace Ignixa.Validation.Tests.Schema;

public class ValidationCanonicalIdentityTests
{
    [Theory]
    [InlineData("Patient")]
    [InlineData("http://hl7.org/fhir/StructureDefinition/Patient")]
    [InlineData("http://hl7.org/fhir/StructureDefinition/Patient|4.0.1")]
    public void GivenAvailableCoreIdentity_WhenResolving_ThenReturnsPatient(string identity)
    {
        var resolver = new StructureDefinitionSchemaResolver(new R4CoreSchemaProvider());
        var schema = resolver.GetSchema(identity);
        schema.ShouldNotBeNull();
        schema.ResourceType.ShouldBe("Patient");
        schema.CanonicalUrl.ShouldBe(identity == "Patient" ? "http://hl7.org/fhir/StructureDefinition/Patient" : identity);
    }

    [Theory]
    [InlineData("https://unrelated.example/StructureDefinition/Patient")]
    [InlineData("urn:example:Patient")]
    [InlineData("http://hl7.org/fhir/StructureDefinition/Patient|5.0.0")]
    [InlineData("http://hl7.org/fhir/StructureDefinition/Patient|")]
    [InlineData("http://hl7.org/fhir/StructureDefinition/extra/Patient")]
    public void GivenUnavailableIdentity_WhenResolving_ThenDoesNotSubstituteByLastSegment(string identity)
    {
        var resolver = new StructureDefinitionSchemaResolver(new R4CoreSchemaProvider());
        resolver.GetSchema(identity).ShouldBeNull();
    }
}
