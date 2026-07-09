/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Executable examples for using FHIR Mapping Language with typed HL7v2 logical models.
 */

using Shouldly;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Integration;

public class Hl7v2MappingLanguageExamplesTests
{
    [Fact]
    public void GivenAdtA01LogicalModelToPatientMap_WhenParsing_ThenCapturesSegmentToPatientRules()
    {
        // Arrange
        var mappingText = """
            map 'http://ignixa.org/fhir/StructureMap/Hl7v2AdtA01ToPatient' = 'Hl7v2AdtA01ToPatient'

            conceptmap '#AdministrativeSex' {
              prefix v2 = 'http://terminology.hl7.org/CodeSystem/v2-0001'
              prefix fhir = 'http://hl7.org/fhir/administrative-gender'

              v2:M == fhir:male
              v2:F == fhir:female
              v2:U == fhir:unknown
            }

            uses 'http://ignixa.org/fhir/StructureDefinition/Hl7v2AdtA01' alias Hl7v2AdtA01 as source
            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as target

            group Hl7v2AdtA01ToPatient(source msg : Hl7v2AdtA01, target patient : Patient) {
              msg.PID.patientIdentifierList as cx -> patient.identifier = create('Identifier') as identifier then MapCxToIdentifier(cx, identifier);
              msg.PID.patientName as xpn -> patient.name = create('HumanName') as name then MapXpnToHumanName(xpn, name);
              msg.PID.dateTimeOfBirth -> patient.birthDate = dateOp(msg.PID.dateTimeOfBirth);
              msg.PID.administrativeSex -> patient.gender = translate(msg.PID.administrativeSex, '#AdministrativeSex', 'code');
            }

            group MapCxToIdentifier(source cx : Cx, target identifier : Identifier) {
              cx.idNumber -> identifier.value;
              cx.assigningAuthority.namespaceId -> identifier.system;
            }

            group MapXpnToHumanName(source xpn : Xpn, target name : HumanName) {
              xpn.familyName.surname -> name.family;
              xpn.givenName -> name.given;
            }
            """;

        var parser = new MappingParser();

        // Act
        var map = parser.Parse(mappingText);

        // Assert
        map.Url.ShouldBe("http://ignixa.org/fhir/StructureMap/Hl7v2AdtA01ToPatient");
        map.ConceptMaps.Count.ShouldBe(1);
        map.Uses.Select(use => use.Alias).ShouldBe(["Hl7v2AdtA01", "Patient"]);
        map.Groups.Select(group => group.Name).ShouldBe([
            "Hl7v2AdtA01ToPatient",
            "MapCxToIdentifier",
            "MapXpnToHumanName"
        ]);

        var mainGroup = map.Groups[0];
        mainGroup.Rules.Count.ShouldBe(4);
        mainGroup.Rules.SelectMany(rule => rule.Targets.Select(target => ToPath(target.Context))).ShouldBe([
            "patient.identifier",
            "patient.name",
            "patient.birthDate",
            "patient.gender"
        ]);

        var genderTransform = (TransformExpression)mainGroup.Rules[3].Targets[0].Transform!;
        genderTransform.FunctionName.ShouldBe("translate");
        genderTransform.Arguments.Count.ShouldBe(3);
    }

    [Fact]
    public void GivenPatientToAdtA08LogicalModelMap_WhenParsing_ThenCapturesPatientToSegmentRules()
    {
        // Arrange
        var mappingText = """
            map 'http://ignixa.org/fhir/StructureMap/PatientToHl7v2AdtA08' = 'PatientToHl7v2AdtA08'

            conceptmap '#AdministrativeSex' {
              prefix fhir = 'http://hl7.org/fhir/administrative-gender'
              prefix v2 = 'http://terminology.hl7.org/CodeSystem/v2-0001'

              fhir:male == v2:M
              fhir:female == v2:F
              fhir:unknown == v2:U
            }

            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
            uses 'http://ignixa.org/fhir/StructureDefinition/Hl7v2AdtA08' alias Hl7v2AdtA08 as target

            group PatientToHl7v2AdtA08(source patient : Patient, target msg : Hl7v2AdtA08) {
              patient -> msg.MSH.messageType.messageCode = 'ADT';
              patient -> msg.MSH.messageType.triggerEvent = 'A08';
              patient.id -> msg.PID.patientIdentifierList = create('Cx') as cx then PatientIdToCx(patient, cx);
              patient.name as name -> msg.PID.patientName = create('Xpn') as xpn then HumanNameToXpn(name, xpn);
              patient.birthDate -> msg.PID.dateTimeOfBirth = dateOp(patient.birthDate);
              patient.gender -> msg.PID.administrativeSex = translate(patient.gender, '#AdministrativeSex', 'code');
            }

            group PatientIdToCx(source patient : Patient, target cx : Cx) {
              patient.id -> cx.idNumber;
            }

            group HumanNameToXpn(source name : HumanName, target xpn : Xpn) {
              name.family -> xpn.familyName.surname;
              name.given -> xpn.givenName;
            }
            """;

        var parser = new MappingParser();

        // Act
        var map = parser.Parse(mappingText);

        // Assert
        map.Url.ShouldBe("http://ignixa.org/fhir/StructureMap/PatientToHl7v2AdtA08");
        map.ConceptMaps.Count.ShouldBe(1);
        map.Uses.Select(use => use.Alias).ShouldBe(["Patient", "Hl7v2AdtA08"]);
        map.Groups.Select(group => group.Name).ShouldBe([
            "PatientToHl7v2AdtA08",
            "PatientIdToCx",
            "HumanNameToXpn"
        ]);

        var mainGroup = map.Groups[0];
        mainGroup.Rules.Count.ShouldBe(6);
        mainGroup.Rules.SelectMany(rule => rule.Targets.Select(target => ToPath(target.Context))).ShouldBe([
            "msg.MSH.messageType.messageCode",
            "msg.MSH.messageType.triggerEvent",
            "msg.PID.patientIdentifierList",
            "msg.PID.patientName",
            "msg.PID.dateTimeOfBirth",
            "msg.PID.administrativeSex"
        ]);

        var messageCode = (LiteralExpression)mainGroup.Rules[0].Targets[0].Transform!;
        messageCode.Value.ShouldBe("ADT");

        var triggerEvent = (LiteralExpression)mainGroup.Rules[1].Targets[0].Transform!;
        triggerEvent.Value.ShouldBe("A08");
    }

    private static string? ToPath(Expression? expression)
    {
        return expression switch
        {
            null => null,
            IdentifierExpression identifier => identifier.Name,
            QualifiedIdentifierExpression qualified => $"{ToPath(qualified.Context)}.{qualified.Property}",
            IndexExpression index => $"{ToPath(index.Context)}[{index.Index}]",
            _ => expression.ToString()
        };
    }
}
