// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;
using R4 = Ignixa.Models.R4;

namespace Ignixa.Models.Spike.Tests;

/// <summary>
/// Hard part 3 (the headline): the typed facade view must AGREE with the schema-aware IElement /
/// FHIRPath runtime over the SAME node. Any divergence is a real bug because typed access and
/// validation/FHIRPath would disagree.
/// </summary>
public sealed class RuntimeAgreementSpikeTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R4.GetSchemaProvider();

    private const string PatientJson =
        """
        {
          "resourceType": "Patient",
          "id": "example",
          "gender": "male",
          "birthDate": "1974-12-25",
          "_birthDate": {
            "extension": [
              {
                "url": "http://hl7.org/fhir/StructureDefinition/patient-birthTime",
                "valueDateTime": "1974-12-25T14:35:45-05:00"
              }
            ]
          }
        }
        """;

    [Fact]
    public void GivenPatient_WhenComparingBirthDate_ThenFacadeAgreesWithFhirPath()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);
        var patient = resource.As<R4.Patient>();
        IElement element = resource.ToElement(Schema);

        var fromFhirPath = element.Scalar("Patient.birthDate")?.ToString();

        patient.BirthDate.ShouldBe(fromFhirPath);
        patient.BirthDate.ShouldBe("1974-12-25");
    }

    [Fact]
    public void GivenPatient_WhenComparingGender_ThenFacadeAgreesWithFhirPath()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);
        var patient = resource.As<R4.Patient>();
        IElement element = resource.ToElement(Schema);

        var fromFhirPath = element.Scalar("Patient.gender")?.ToString();

        patient.Gender!.Value.GetLiteral().ShouldBe(fromFhirPath);
    }

    [Fact]
    public void GivenPrimitiveShadow_WhenComparingExtension_ThenWrapperAgreesWithFhirPath()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);
        var patient = resource.As<R4.Patient>();
        IElement element = resource.ToElement(Schema);

        var fhirPathExtensions = element.Select("Patient.birthDate.extension").ToList();
        var fhirPathUrl = element.Scalar("Patient.birthDate.extension.url")?.ToString();

        fhirPathExtensions.Count.ShouldBe(patient.BirthDateElement.Extension.Count);
        fhirPathUrl.ShouldBe(patient.BirthDateElement.Extension[0]!["url"]!.GetValue<string>());
    }

    [Fact]
    public void GivenObservationQuantity_WhenComparingChoice_ThenFacadeAgreesWithFhirPath()
    {
        var resource = ResourceJsonNode.Parse(
            """
            {
              "resourceType": "Observation",
              "status": "final",
              "valueQuantity": { "value": 185, "unit": "cm", "code": "cm" }
            }
            """);
        var obs = resource.As<R4.Observation>();
        IElement element = resource.ToElement(Schema);

        var ofTypeQuantity = element.Select("Observation.value.ofType(Quantity)").ToList();
        var valueChoice = element.Children("value").ToList();
        var quantityValue = element.Scalar("Observation.value.ofType(Quantity).value");

        (obs.ValueType == R4.ObservationValueType.Quantity).ShouldBeTrue();
        ofTypeQuantity.Count.ShouldBe(1);
        valueChoice.Count.ShouldBe(1);
        valueChoice[0].Name.ShouldBe("valueQuantity");
        Convert.ToDecimal(quantityValue).ShouldBe(obs.ValueQuantity!.Value!.Value);
    }

    [Fact]
    public void GivenObservationString_WhenComparingChoiceVariant_ThenOnlyMatchingVariantSeen()
    {
        var resource = ResourceJsonNode.Parse(
            """
            {
              "resourceType": "Observation",
              "status": "final",
              "valueString": "borderline"
            }
            """);
        var obs = resource.As<R4.Observation>();
        IElement element = resource.ToElement(Schema);

        var asQuantity = element.Select("Observation.value.ofType(Quantity)").ToList();
        var asString = element.Select("Observation.value.ofType(string)").ToList();
        var valueChoice = element.Children("value").ToList();

        obs.ValueType.ShouldBe(R4.ObservationValueType.String);
        asQuantity.ShouldBeEmpty();
        asString.Count.ShouldBe(1);
        asString[0].Value.ShouldBe(obs.ValueString);
        valueChoice[0].Name.ShouldBe("valueString");
    }

    [Fact]
    public void GivenTypedMutation_WhenReEvaluatingFhirPath_ThenRuntimeSeesTheMutation()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);
        var patient = resource.As<R4.Patient>();

        patient.BirthDate = "2001-09-11";
        resource.InvalidateCaches();

        IElement element = resource.ToElement(Schema);
        element.Scalar("Patient.birthDate")?.ToString().ShouldBe("2001-09-11");
    }
}
