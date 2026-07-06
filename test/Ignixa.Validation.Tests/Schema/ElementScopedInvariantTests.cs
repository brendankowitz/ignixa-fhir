// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Schema;

/// <summary>
/// Verifies that element-scoped FHIRPath invariants are evaluated at the altitude of the element
/// that owns them, not hoisted to the resource root. pat-1 is defined on Patient.contact
/// ("SHALL at least contain a contact's details or a reference to an organization") and must run
/// once per contact - in that contact's FHIRPath context - and not at all when there is no contact.
/// </summary>
public class ElementScopedInvariantTests
{
    private readonly ISchema _schema;
    private readonly IValidationSchemaResolver _schemaResolver;

    public ElementScopedInvariantTests()
    {
        _schema = new R4CoreSchemaProvider();
        _schemaResolver = new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(_schema));
    }

    private ValidationResult ValidatePatient(string patientJson)
    {
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(patientJson)!);
        var schema = _schemaResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient");
        schema.ShouldNotBeNull();

        var settings = new ValidationSettings { Depth = ValidationDepth.Full };
        return schema!.Validate(sourceNode.ToElement(TestSchemaProvider.GetR4Schema()), settings, new ValidationState());
    }

    [Fact]
    public void GivenPatientWithoutContact_WhenValidatingAtFullDepth_ThenPat1DoesNotFire()
    {
        // Arrange - no name and no contact: the old root-hoisted evaluation resolved name.exists()
        // against Patient.name (absent) and emitted a spurious pat-1 error.
        var patient = """
        {
          "resourceType": "Patient",
          "active": true
        }
        """;

        // Act
        var result = ValidatePatient(patient);

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "pat-1");
    }

    [Fact]
    public void GivenPatientWithEmptyContact_WhenValidatingAtFullDepth_ThenPat1FiresAtContactAltitude()
    {
        // Arrange - a contact carrying none of name/telecom/address/organization violates pat-1.
        var patient = """
        {
          "resourceType": "Patient",
          "contact": [
            { "gender": "female" }
          ]
        }
        """;

        // Act
        var result = ValidatePatient(patient);

        // Assert
        var pat1Issues = result.Issues.Where(i => i.Code == "pat-1").ToList();
        pat1Issues.ShouldHaveSingleItem();
        pat1Issues[0].Severity.ShouldBe(IssueSeverity.Error);
        pat1Issues[0].Path.ShouldContain("contact");
    }

    [Fact]
    public void GivenPatientWithValidContact_WhenValidatingAtFullDepth_ThenPat1DoesNotFire()
    {
        // Arrange - contact.organization satisfies pat-1 even though the Patient itself has no name.
        var patient = """
        {
          "resourceType": "Patient",
          "contact": [
            { "organization": { "reference": "Organization/1" } }
          ]
        }
        """;

        // Act
        var result = ValidatePatient(patient);

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "pat-1");
    }

    [Fact]
    public void GivenPatientWithTwoContactsOnlyOneOffending_WhenValidatingAtFullDepth_ThenPat1FiresOnceAtOffendingOccurrence()
    {
        // Arrange - contact[0] satisfies pat-1 via organization; contact[1] carries none of
        // name/telecom/address/organization. Multi-occurrence guard: element-scoped evaluation must
        // run per-occurrence, not just once for the first/any match.
        var patient = """
        {
          "resourceType": "Patient",
          "contact": [
            { "organization": { "reference": "Organization/1" } },
            { "gender": "female" }
          ]
        }
        """;

        // Act
        var result = ValidatePatientWithRootScope(patient);

        // Assert
        var pat1Issues = result.Issues.Where(i => i.Code == "pat-1").ToList();
        pat1Issues.ShouldHaveSingleItem();
        pat1Issues[0].Path.ShouldContain("contact[1]");
    }

    [Fact]
    public void GivenValueSetComposeIncludeWithoutValueSetOrSystem_WhenValidatingAtFullDepth_ThenVsd1FiresAtComposeIncludeAltitude()
    {
        // Arrange - vsd-1 lives two backbone levels deep: ValueSet.compose.include. An include entry
        // with neither valueSet nor system violates it.
        var valueSet = """
        {
          "resourceType": "ValueSet",
          "status": "active",
          "compose": {
            "include": [
              { "concept": [{ "code": "foo" }] }
            ]
          }
        }
        """;

        // Act
        var result = ValidateValueSetWithRootScope(valueSet);

        // Assert
        var vsd1Issues = result.Issues.Where(i => i.Code == "vsd-1").ToList();
        vsd1Issues.ShouldHaveSingleItem();
        vsd1Issues[0].Path.ShouldContain("compose.include[0]");
    }

    [Fact]
    public void GivenValueSetWithoutCompose_WhenValidatingAtFullDepth_ThenVsd1DoesNotFire()
    {
        // Arrange - no compose element at all: the nested "include" altitude is never entered, so
        // vsd-1 must not fire (a root-hoisted evaluation would have wrongly fired here).
        var valueSet = """
        {
          "resourceType": "ValueSet",
          "status": "active"
        }
        """;

        // Act
        var result = ValidateValueSetWithRootScope(valueSet);

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "vsd-1");
    }

    [Fact]
    public void GivenBundleEntryWithoutResourceRequestOrResponse_WhenValidatingAtFullDepth_ThenBdl5FiresAtEntryAltitude()
    {
        // Arrange - bdl-5 lives on Bundle.entry, a distinct constraint shape from pat-1 (per-entry,
        // one backbone level), proving the element-scoped fix generalizes beyond Patient.contact.
        var bundle = """
        {
          "resourceType": "Bundle",
          "type": "collection",
          "entry": [
            { "fullUrl": "urn:uuid:1" }
          ]
        }
        """;

        // Act
        var result = ValidateBundleWithRootScope(bundle);

        // Assert
        var bdl5Issues = result.Issues.Where(i => i.Code == "bdl-5").ToList();
        bdl5Issues.ShouldHaveSingleItem();
        bdl5Issues[0].Path.ShouldContain("entry[0]");
    }

    [Fact]
    public void GivenBundleEntryWithResource_WhenValidatingAtFullDepth_ThenBdl5DoesNotFire()
    {
        // Arrange
        var bundle = """
        {
          "resourceType": "Bundle",
          "type": "collection",
          "entry": [
            { "resource": { "resourceType": "Patient", "id": "p1" } }
          ]
        }
        """;

        // Act
        var result = ValidateBundleWithRootScope(bundle);

        // Assert
        result.Issues.ShouldNotContain(i => i.Code == "bdl-5");
    }

    private ValidationResult ValidatePatientWithRootScope(string patientJson) =>
        ValidateWithRootScope("http://hl7.org/fhir/StructureDefinition/Patient", patientJson);

    private ValidationResult ValidateValueSetWithRootScope(string valueSetJson) =>
        ValidateWithRootScope("http://hl7.org/fhir/StructureDefinition/ValueSet", valueSetJson);

    private ValidationResult ValidateBundleWithRootScope(string bundleJson) =>
        ValidateWithRootScope("http://hl7.org/fhir/StructureDefinition/Bundle", bundleJson);

    private ValidationResult ValidateWithRootScope(string canonicalUrl, string resourceJson)
    {
        var sourceNode = JsonNodeSourceNode.Create(JsonNode.Parse(resourceJson)!);
        var schema = _schemaResolver.GetSchema(canonicalUrl);
        schema.ShouldNotBeNull();

        var settings = new ValidationSettings { Depth = ValidationDepth.Full };
        var element = sourceNode.ToElement(TestSchemaProvider.GetR4Schema());
        var state = new ValidationState().EnterRootResource(element);
        return schema!.Validate(element, settings, state);
    }
}
