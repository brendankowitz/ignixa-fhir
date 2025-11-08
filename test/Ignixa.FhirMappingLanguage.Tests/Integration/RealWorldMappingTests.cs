/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Integration tests with real-world FHIR mapping examples.
 * Based on FHIR cross-version mapping specifications.
 */

using FluentAssertions;
using Ignixa.FhirMappingLanguage.Expressions;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Integration;

public class RealWorldMappingTests
{
    #region Tutorial Example 1: Simple Patient to Bundle

    [Fact]
    public void GivenTutorialExample1_WhenParsing_ThenParsesSuccessfully()
    {
        // Arrange - From FHIR Mapping Tutorial
        var mappingText = @"
map 'http://hl7.org/fhir/tutorial/map1' = 'tutorial1'

uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Bundle' alias Bundle as target

group PatientToBundle(source patient : Patient, target bundle : Bundle) {
  patient -> bundle.entry as entry then {
    patient -> entry.resource = create('Patient') as tgt then PatientContent(patient, tgt);
  };
}

group PatientContent(source src : Patient, target tgt : Patient) {
  src.identifier -> tgt.identifier;
  src.name -> tgt.name;
  src.telecom -> tgt.telecom;
}
";
        var compiler = new MappingCompiler();

        // Act
        var result = compiler.Parse(mappingText);

        // Assert
        result.Should().NotBeNull();
        result.Groups.Should().HaveCount(2);
        result.Groups[0].Name.Should().Be("PatientToBundle");
        result.Groups[1].Name.Should().Be("PatientContent");
    }

    #endregion

    #region Tutorial Example 2: With Type Constraints

    [Fact]
    public void GivenTutorialExample2_WhenParsing_ThenParsesSuccessfully()
    {
        // Arrange - Example with type constraints
        var mappingText = @"
map 'http://hl7.org/fhir/tutorial/map2' = 'tutorial2'

uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias PatientR5 as target

group PatientToPatient(source src : Patient, target tgt : PatientR5) {
  src.name : HumanName as vn -> tgt.name = create('HumanName') as tn then {
    vn.given -> tn.given;
    vn.family -> tn.family;
  };
}
";
        var compiler = new MappingCompiler();

        // Act
        var result = compiler.Parse(mappingText);

        // Assert
        result.Should().NotBeNull();
        result.Groups[0].Rules[0].Sources[0].Type.Should().Be("HumanName");
    }

    #endregion

    #region Tutorial Example 3: With List Modes

    [Fact]
    public void GivenTutorialExample3WithListModes_WhenParsing_ThenParsesSuccessfully()
    {
        // Arrange - Example with list modes
        var mappingText = @"
map 'http://hl7.org/fhir/tutorial/map3' = 'tutorial3'

uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias PatientR5 as target

group PatientToPatient(source src : Patient, target tgt : PatientR5) {
  src.name as vn -> tgt.name = create('HumanName') as tn first then {
    vn.given -> tn.given;
    vn.family -> tn.family;
  };
}
";
        var compiler = new MappingCompiler();

        // Act
        var result = compiler.Parse(mappingText);

        // Assert
        result.Should().NotBeNull();
        result.Groups[0].Rules[0].Targets[0].ListMode.Should().NotBeNull();
    }

    #endregion

    #region Cross-Version Example: R4 to R5 Patient

    [Fact]
    public void GivenR4ToR5PatientMapping_WhenParsing_ThenParsesSuccessfully()
    {
        // Arrange - Simplified from HL7 cross-version mapping pack
        var mappingText = @"
map 'http://hl7.org/fhir/StructureMap/Patient4to5' = 'Patient4to5'

uses 'http://hl7.org/fhir/4.0/StructureDefinition/Patient' alias PatientR4 as source
uses 'http://hl7.org/fhir/5.0/StructureDefinition/Patient' alias PatientR5 as target

group Patient(source src : PatientR4, target tgt : PatientR5) extends DomainResource {
  src.identifier -> tgt.identifier;
  src.active -> tgt.active;
  src.name -> tgt.name;
  src.telecom -> tgt.telecom;
  src.gender -> tgt.gender;
  src.birthDate -> tgt.birthDate;
  src.deceased : boolean -> tgt.deceased;
  src.deceased : dateTime -> tgt.deceased;
  src.address -> tgt.address;
  src.maritalStatus -> tgt.maritalStatus;
  src.multipleBirth : boolean -> tgt.multipleBirth;
  src.multipleBirth : integer -> tgt.multipleBirth;
  src.photo -> tgt.photo;
}
";
        var compiler = new MappingCompiler();

        // Act
        var result = compiler.Parse(mappingText);

        // Assert
        result.Should().NotBeNull();
        result.Url.Should().Be("http://hl7.org/fhir/StructureMap/Patient4to5");
        result.Groups[0].Extends.Should().Be("DomainResource");
        result.Groups[0].Rules.Should().HaveCountGreaterThan(5);
    }

    #endregion

    #region Complex Nested Rules Example

    [Fact]
    public void GivenComplexNestedRules_WhenParsing_ThenParsesSuccessfully()
    {
        // Arrange - Complex nested transformation
        var mappingText = @"
map 'http://example.org/complex' = 'complex'

uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Bundle' alias Bundle as target

group PatientToBundle(source patient : Patient, target bundle : Bundle) {
  patient -> bundle.type = 'collection';

  patient -> bundle.entry as entry then {
    patient -> entry.resource = create('Patient') as tgt then {
      patient.id -> tgt.id;

      patient.name as vn -> tgt.name as tn then {
        vn.use -> tn.use;
        vn.text -> tn.text;
        vn.family -> tn.family;

        vn.given as vg -> tn.given = vg then {
          vg -> tn.given;
        };

        vn.prefix as vp -> tn.prefix = vp;
        vn.suffix as vs -> tn.suffix = vs;
      };

      patient.telecom as vt -> tgt.telecom as tt then {
        vt.system -> tt.system;
        vt.value -> tt.value;
        vt.use -> tt.use;
      };

      patient.gender -> tgt.gender;
      patient.birthDate -> tgt.birthDate;
    };
  };
}
";
        var compiler = new MappingCompiler();

        // Act
        var result = compiler.Parse(mappingText);

        // Assert
        result.Should().NotBeNull();
        result.Groups[0].Rules.Should().HaveCountGreaterThan(1);

        // Verify nested structure
        var firstRule = result.Groups[0].Rules.First(r => r.Dependent != null);
        firstRule.Dependent.Should().NotBeNull();
        firstRule.Dependent.Should().BeOfType<RuleSetExpression>();
        var ruleSet = (RuleSetExpression)firstRule.Dependent!;
        ruleSet.Rules.Should().NotBeEmpty();
    }

    #endregion

    #region Transform Functions Example

    [Fact]
    public void GivenVariousTransformFunctions_WhenParsing_ThenParsesSuccessfully()
    {
        // Arrange - Example with various transform functions
        var mappingText = @"
map 'http://example.org/transforms' = 'transforms'

uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias PatientOut as target

group TransformExamples(source src : Patient, target tgt : PatientOut) {
  src -> tgt.id = uuid();
  src.name -> tgt.name = copy(src.name);
  src.identifier -> tgt.identifier = create('Identifier');
  src.gender -> tgt.gender = cast(src.gender, 'code');
  src.birthDate -> tgt.birthDate = truncate(src.birthDate, 10);
}
";
        var compiler = new MappingCompiler();

        // Act
        var result = compiler.Parse(mappingText);

        // Assert
        result.Should().NotBeNull();
        result.Groups[0].Rules.Should().HaveCountGreaterThan(3);

        // Verify transform functions
        result.Groups[0].Rules
            .Where(r => r.Targets.Any(t => t.Transform != null))
            .Should().HaveCountGreaterThan(3);
    }

    #endregion

    #region Multiple Groups with Extends

    [Fact]
    public void GivenMultipleGroupsWithInheritance_WhenParsing_ThenParsesSuccessfully()
    {
        // Arrange - Example with group inheritance
        var mappingText = @"
map 'http://example.org/inheritance' = 'inheritance'

uses 'http://hl7.org/fhir/StructureDefinition/DomainResource' alias DomainResource as source
uses 'http://hl7.org/fhir/StructureDefinition/DomainResource' alias DomainResourceOut as target
uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias PatientOut as target

group DomainResource(source src : DomainResource, target tgt : DomainResourceOut) {
  src.id -> tgt.id;
  src.meta -> tgt.meta;
  src.text -> tgt.text;
}

group Patient(source src : Patient, target tgt : PatientOut) extends DomainResource {
  src.identifier -> tgt.identifier;
  src.name -> tgt.name;
  src.gender -> tgt.gender;
}
";
        var compiler = new MappingCompiler();

        // Act
        var result = compiler.Parse(mappingText);

        // Assert
        result.Should().NotBeNull();
        result.Groups.Should().HaveCount(2);
        result.Groups[0].Name.Should().Be("DomainResource");
        result.Groups[1].Name.Should().Be("Patient");
        result.Groups[1].Extends.Should().Be("DomainResource");
    }

    #endregion

    // NOTE: Named rules using :: syntax removed - not part of FHIR Mapping Language spec
    // The FHIR spec uses trailing quoted strings for rule documentation, not rule names

    #region Multiple Sources and Targets

    [Fact]
    public void GivenMultipleSourcesAndTargets_WhenParsing_ThenParsesSuccessfully()
    {
        // Arrange - Example with multiple sources and targets
        var mappingText = @"
map 'http://example.org/multiple' = 'multiple'

uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Observation' alias Observation as source
uses 'http://hl7.org/fhir/StructureDefinition/Bundle' alias Bundle as target

group MultiSource(source patient : Patient, source obs : Observation, target bundle : Bundle) {
  patient, obs -> bundle.entry as patientEntry, bundle.entry as obsEntry;
  patient.id -> patientEntry.resource;
  obs.id -> obsEntry.resource;
}
";
        var compiler = new MappingCompiler();

        // Act
        var result = compiler.Parse(mappingText);

        // Assert
        result.Should().NotBeNull();
        result.Groups[0].Parameters.Should().HaveCount(3);
        result.Groups[0].Rules[0].Sources.Should().HaveCount(2);
        result.Groups[0].Rules[0].Targets.Should().HaveCount(2);
    }

    #endregion
}
