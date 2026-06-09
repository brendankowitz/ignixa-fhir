// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Shouldly;
using Xunit;

namespace Ignixa.PackageManagement.Tests;

/// <summary>
/// MVP tests for <see cref="StructureDefinitionTypeAdapter"/> against a hand-written
/// base-spec Patient snapshot. Covers tree-building, direct children, collection vs scalar,
/// and required terminology binding extraction.
/// </summary>
public class StructureDefinitionTypeAdapterTests
{
    private static readonly string[] ExpectedPatientChildren = { "id", "active", "name", "gender", "birthDate" };

    private static string LoadFixture(string name)
        => File.ReadAllText(Path.Combine("TestData", "StructureDefinitions", name));

    [Fact]
    public void GivenPatientSnapshot_WhenAdapted_ThenRootTypeNameIsPatient()
    {
        var json = LoadFixture("PatientMinimal.json");
        var adapter = new StructureDefinitionTypeAdapter();

        var type = adapter.Adapt(json, fhirVersion: "4.0.1");

        type.ShouldNotBeNull();
        type!.Info.Name.ShouldBe("Patient");
        type.Info.IsResource.ShouldBeTrue();
        type.IsCollection.ShouldBeFalse();
    }

    [Fact]
    public void GivenPatientSnapshot_WhenAdapted_ThenExposesDirectChildrenInDeclaredOrder()
    {
        var type = new StructureDefinitionTypeAdapter().Adapt(LoadFixture("PatientMinimal.json"), "4.0.1")!;

        type.Children.Select(c => c.Info.Name).ShouldBe(ExpectedPatientChildren);
    }

    [Fact]
    public void GivenPatientNameElement_WhenAdapted_ThenIsCollection()
    {
        var type = new StructureDefinitionTypeAdapter().Adapt(LoadFixture("PatientMinimal.json"), "4.0.1")!;
        var name = type.Children.Single(c => c.Info.Name == "name");

        name.IsCollection.ShouldBeTrue();
    }

    [Fact]
    public void GivenPatientIdElement_WhenAdapted_ThenIsScalar()
    {
        var type = new StructureDefinitionTypeAdapter().Adapt(LoadFixture("PatientMinimal.json"), "4.0.1")!;
        var id = type.Children.Single(c => c.Info.Name == "id");

        id.IsCollection.ShouldBeFalse();
    }

    [Fact]
    public void GivenPatientGenderBinding_WhenAdapted_ThenExposesRequiredBinding()
    {
        var type = (ITypeExtended)new StructureDefinitionTypeAdapter().Adapt(LoadFixture("PatientMinimal.json"), "4.0.1")!;
        var gender = (ITypeExtended)type.Children.Single(c => c.Info.Name == "gender");

        gender.Binding.ShouldNotBeNull();
        gender.Binding!.Strength.ShouldBe("required");
        gender.Binding.ValueSet.ShouldStartWith("http://hl7.org/fhir/ValueSet/administrative-gender");
    }

    [Fact]
    public void GivenPatientGenderType_WhenAdapted_ThenTypeIsCode()
    {
        var type = (ITypeExtended)new StructureDefinitionTypeAdapter().Adapt(LoadFixture("PatientMinimal.json"), "4.0.1")!;
        var gender = (ITypeExtended)type.Children.Single(c => c.Info.Name == "gender");

        gender.Types.ShouldNotBeEmpty();
        gender.Types[0].Code.ShouldBe("code");
    }

    [Fact]
    public void GivenNonStructureDefinitionJson_WhenAdapted_ThenReturnsNull()
    {
        var result = new StructureDefinitionTypeAdapter().Adapt("{\"resourceType\":\"Patient\",\"id\":\"x\"}", "4.0.1");
        result.ShouldBeNull();
    }

    // ===== Primitive type mapping — regression pins for FhirPrimitiveExtensions delegation =====

    private const string Integer64StructureDefinitionJson = """
        {
          "resourceType": "StructureDefinition",
          "id": "integer64",
          "url": "http://hl7.org/fhir/StructureDefinition/integer64",
          "version": "5.0.0",
          "name": "integer64",
          "status": "active",
          "kind": "primitive-type",
          "abstract": false,
          "type": "integer64",
          "snapshot": {
            "element": [
              {
                "id": "integer64",
                "path": "integer64",
                "min": 0,
                "max": "*",
                "type": [ { "code": "integer64" } ]
              },
              {
                "id": "integer64.value",
                "path": "integer64.value",
                "min": 0,
                "max": "1",
                "type": [ { "code": "integer64" } ]
              }
            ]
          }
        }
        """;

    [Fact]
    public void GivenInteger64StructureDefinition_WhenAdapted_ThenRootIsPrimitive()
    {
        var type = new StructureDefinitionTypeAdapter().Adapt(Integer64StructureDefinitionJson, "5.0.0")!;

        type.ShouldNotBeNull();
        type.Info.Primitive.ShouldBe(FhirPrimitive.Integer64);
    }

    [Fact]
    public void GivenBooleanElement_WhenAdapted_ThenPrimitiveIsBoolean()
    {
        var type = new StructureDefinitionTypeAdapter().Adapt(LoadFixture("PatientMinimal.json"), "4.0.1")!;
        var active = (ITypeExtended)type.Children.Single(c => c.Info.Name == "active");

        active.Info.Primitive.ShouldBe(FhirPrimitive.Boolean);
    }

    [Fact]
    public void GivenDateElement_WhenAdapted_ThenPrimitiveIsDate()
    {
        var type = new StructureDefinitionTypeAdapter().Adapt(LoadFixture("PatientMinimal.json"), "4.0.1")!;
        var birthDate = (ITypeExtended)type.Children.Single(c => c.Info.Name == "birthDate");

        birthDate.Info.Primitive.ShouldBe(FhirPrimitive.Date);
    }

    // ===== Constraints, choice types, fixed/pattern, reference targets =====

    [Fact]
    public void GivenObservationRoot_WhenAdapted_ThenCarriesEle1Constraint()
    {
        var type = (ITypeExtended)new StructureDefinitionTypeAdapter().Adapt(LoadFixture("ObservationMinimal.json"), "4.0.1")!;
        type.Constraints.ShouldContain(c => c.Key == "ele-1");
    }

    [Fact]
    public void GivenObservationRoot_WhenAdapted_ThenPreservesConstraintSeverity()
    {
        var type = (ITypeExtended)new StructureDefinitionTypeAdapter().Adapt(LoadFixture("ObservationMinimal.json"), "4.0.1")!;
        type.Constraints.Single(c => c.Key == "ele-1").Severity.ShouldBe("error");
        type.Constraints.Single(c => c.Key == "obs-3").Severity.ShouldBe("warning");
    }

    [Fact]
    public void GivenObservationValueChoice_WhenAdapted_ThenExposesMultipleTypes()
    {
        var type = new StructureDefinitionTypeAdapter().Adapt(LoadFixture("ObservationMinimal.json"), "4.0.1")!;
        var choice = (ITypeExtended)type.Children.Single(c => c.Info.Name == "value[x]");

        var codes = choice.Types.Select(t => t.Code).ToList();
        codes.ShouldContain("Quantity");
        codes.ShouldContain("CodeableConcept");
        codes.ShouldContain("string");
        choice.Info.IsChoiceElement.ShouldBeTrue();
    }

    [Fact]
    public void GivenObservationStatusFixedCode_WhenAdapted_ThenExposesFixedValue()
    {
        var type = new StructureDefinitionTypeAdapter().Adapt(LoadFixture("ObservationMinimal.json"), "4.0.1")!;
        var status = (ITypeExtended)type.Children.Single(c => c.Info.Name == "status");

        // FixedValue is stored as JSON-encoded form (so consumers can JsonNode.Parse it).
        status.FixedValue.ShouldBe("\"final\"");
        status.IsRequired.ShouldBeTrue();
    }

    [Fact]
    public void GivenObservationCodePatternCodeableConcept_WhenAdapted_ThenExposesPatternValue()
    {
        var type = new StructureDefinitionTypeAdapter().Adapt(LoadFixture("ObservationMinimal.json"), "4.0.1")!;
        var code = (ITypeExtended)type.Children.Single(c => c.Info.Name == "code");

        code.PatternValue.ShouldNotBeNull();
        code.PatternValue!.ToString()!.ShouldContain("loinc.org");
    }

    [Fact]
    public void GivenObservationSubjectReference_WhenAdapted_ThenExposesTargetProfiles()
    {
        var type = new StructureDefinitionTypeAdapter().Adapt(LoadFixture("ObservationMinimal.json"), "4.0.1")!;
        var subject = (ITypeExtended)type.Children.Single(c => c.Info.Name == "subject");

        subject.ReferenceTargets.ShouldContain("Patient");
        subject.ReferenceTargets.ShouldContain("Group");
    }
}
