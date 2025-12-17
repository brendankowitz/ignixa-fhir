// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests.Scenarios;

/// <summary>
/// Tests for ScenarioBuilder integration with PatientBuilder.
/// Validates the fluent API for creating patients with sophisticated demographics.
/// </summary>
public class ScenarioBuilderPatientBuilderTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    #region WithPatient(Action<PatientBuilder>) Tests

    [Fact]
    public void GivenScenarioBuilder_WhenUsingWithPatientAction_ThenCreatesPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithName("Patient Builder Action Test")
            .WithPatient(p => p
                .WithAge(45)
                .WithGender(g => g.Male)
                .WithGivenName("John")
                .WithFamilyName("Smith"))
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Patient!.ResourceType.ShouldBe("Patient");
        scenario.Patient.MutableNode["gender"]?.GetValue<string>().ShouldBe("male");

        var name = scenario.Patient.MutableNode["name"]?.AsArray()?[0]?.AsObject();
        name?["family"]?.GetValue<string>().ShouldBe("Smith");
        name?["given"]?.AsArray()?[0]?.GetValue<string>().ShouldBe("John");
    }

    [Fact]
    public void GivenScenarioBuilder_WhenUsingWithPatientAction_ThenSetsAgeAttribute()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(p => p.WithAge(50).WithGender("female"))
            .Build();

        // Assert
        scenario.Attributes.ShouldContainKey("age");
        scenario.GetAttribute<int>("age").ShouldBe(50);
    }

    [Fact]
    public void GivenScenarioBuilder_WhenUsingWithPatientAction_ThenSetsGenderAttribute()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(p => p.WithAge(30).WithGender(g => g.Female))
            .Build();

        // Assert
        scenario.Attributes.ShouldContainKey("gender");
        scenario.GetAttribute<string>("gender").ShouldBe("female");
    }

    #endregion

    #region WithPatient Tests

    [Fact]
    public void GivenScenarioBuilder_WhenUsingWithPatient_ThenCreatesPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(p => p
                .WithAge(30)
                .WithGender(g => g.Female)
                .WithAddress("123 Main St", "Seattle", "WA", "98101"))
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Patient!.ResourceType.ShouldBe("Patient");
        scenario.Patient.MutableNode["gender"]?.GetValue<string>().ShouldBe("female");

        var address = scenario.Patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().ShouldBe("Seattle");
        address?["state"]?.GetValue<string>().ShouldBe("WA");
    }

    [Fact]
    public void GivenScenarioBuilder_WhenUsingWithPatientAndStartDate_ThenUsesStartDate()
    {
        // Arrange
        var startDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(p => p.WithAge(40).WithGender("male"), startDate)
            .Build();

        // Assert
        scenario.CurrentTime.ShouldBe(startDate);
    }

    #endregion

    #region WithPatient Tests

    [Fact]
    public void GivenScenarioBuilder_WhenUsingWithPatient_ThenCreatesRealisticPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(p => p
                .FromCity(KnownCities.Boston)
                .WithAge(45)
                .WithRealisticBMI())
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Patient!.ResourceType.ShouldBe("Patient");

        // Should have Boston address
        var address = scenario.Patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().ShouldBe("Boston");
        address?["state"]?.GetValue<string>().ShouldBe("Massachusetts");
        address?["postalCode"]?.GetValue<string>().ShouldStartWith("02");

        // Should have BMI extension
        scenario.Patient.MutableNode["extension"].ShouldNotBeNull();
    }

    [Fact]
    public void GivenScenarioBuilder_WhenUsingWithPatient_ThenSetsEthnicityAttribute()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(p => p.FromCity(KnownCities.Chicago))
            .Build();

        // Assert
        // Ethnicity should be set from city demographics
        scenario.Attributes.ShouldContainKey("ethnicity");
        scenario.GetAttribute<string>("ethnicity").ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void GivenScenarioBuilder_WhenUsingWithPatient_ThenSetsZipCodeAttribute()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(p => p.FromCity(KnownCities.NewYork))
            .Build();

        // Assert
        scenario.Attributes.ShouldContainKey("zipCode");
        scenario.GetAttribute<string>("zipCode").ShouldStartWith("10");
    }

    #endregion

    #region WithSeattlePatient Tests

    [Fact]
    public void GivenScenarioBuilder_WhenUsingWithSeattlePatient_ThenCreatesSeattlePatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithSeattlePatient()
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Patient!.ResourceType.ShouldBe("Patient");

        // Should have Seattle address
        var address = scenario.Patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().ShouldBe("Seattle");
        address?["state"]?.GetValue<string>().ShouldBe("Washington");
    }

    [Fact]
    public void GivenScenarioBuilder_WhenUsingWithSeattlePatientWithConfigure_ThenAppliesConfiguration()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithSeattlePatient(p => p.WithAge(35).WithRealisticBMI())
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.GetAttribute<int>("age").ShouldBe(35);
        scenario.Attributes.ShouldContainKey("bmi");
    }

    #endregion

    #region WithPatientFromCity Tests

    [Fact]
    public void GivenScenarioBuilder_WhenUsingWithPatientFromCity_ThenCreatesPatientFromCity()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatientFromCity(KnownCities.LosAngeles)
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Patient!.ResourceType.ShouldBe("Patient");

        // Should have Los Angeles address
        var address = scenario.Patient.MutableNode["address"]?.AsArray()?[0]?.AsObject();
        address?["city"]?.GetValue<string>().ShouldBe("Los Angeles");
        address?["state"]?.GetValue<string>().ShouldBe("California");
    }

    [Fact]
    public void GivenScenarioBuilder_WhenUsingWithPatientFromCityWithConfigure_ThenAppliesConfiguration()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatientFromCity(
                KnownCities.Philadelphia,
                p => p.WithAge(28).WithGender(g => g.Male))
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Patient!.MutableNode["gender"]?.GetValue<string>().ShouldBe("male");
        scenario.GetAttribute<int>("age").ShouldBe(28);
    }

    #endregion

    #region Integration with Scenario States Tests

    [Fact]
    public void GivenScenarioBuilder_WhenChainingPatientBuilderWithEncounters_ThenBuildsCompleteScenario()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithName("Complete Scenario with PatientBuilder")
            .WithPatient(p => p
                .FromCity(KnownCities.Boston)
                .WithAge(55)
                .WithGender(g => g.Male))
            .AddEncounter("Initial consultation")
            .AddObservation(FhirCode.Observations.BloodGlucose, 120m, "mg/dL")
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Encounters.Count.ShouldBe(1);
        scenario.Observations.Count.ShouldBe(1);

        // Observation should reference the patient
        var observation = scenario.Observations[0];
        var subjectRef = observation.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef!.ShouldContain(scenario.Patient!.Id);
    }

    [Fact]
    public void GivenScenarioBuilder_WhenUsingPatientBuilderWithTag_ThenAppliesTagToPatient()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();

        // Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithTag(tag)
            .WithPatient(p => p.WithAge(40).WithGender("female"))
            .Build();

        // Assert
        scenario.Patient.ShouldNotBeNull();
        scenario.Patient!.MutableNode["meta"]?["tag"].ShouldNotBeNull();

        var tags = scenario.Patient.MutableNode["meta"]?["tag"]?.AsArray();
        tags.ShouldNotBeNull();
        tags.Count.ShouldBeGreaterThanOrEqualTo(1);

        var metaTag = tags?[0]?.AsObject();
        metaTag?["code"]?.GetValue<string>().ShouldBe(tag);
    }

    [Fact]
    public void GivenScenarioBuilder_WhenBuildingMultipleScenariosWithPatientBuilder_ThenGeneratesUniquePatients()
    {
        // Arrange & Act
        var scenario1 = new ScenarioBuilder(_schemaProvider)
            .WithPatient(p => p.FromCity(KnownCities.Boston))
            .Build();

        var scenario2 = new ScenarioBuilder(_schemaProvider)
            .WithPatient(p => p.FromCity(KnownCities.Boston))
            .Build();

        // Assert
        scenario1.Patient!.Id.ShouldNotBe(scenario2.Patient!.Id);
    }

    [Fact]
    public void GivenScenarioBuilder_WhenPatientBuilderWithCondition_ThenConditionReferencesPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithSeattlePatient(p => p.WithAge(60))
            .AddConditionOnset(FhirCode.Conditions.Hypertension)
            .Build();

        // Assert
        scenario.Conditions.Count.ShouldBe(1);
        var condition = scenario.Conditions[0];
        var subjectRef = condition.MutableNode["subject"]?["reference"]?.GetValue<string>();
        subjectRef.ShouldBe($"urn:uuid:{scenario.Patient!.Id}");
    }

    #endregion

    #region ToBundle Tests

    [Fact]
    public void GivenScenarioWithPatientBuilder_WhenConvertingToBundle_ThenPatientIsFirst()
    {
        // Arrange
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient(p => p.FromCity(KnownCities.Chicago).WithAge(45))
            .AddEncounter("Visit")
            .Build();

        // Act
        var bundle = scenario.ToBundle();

        // Assert
        bundle.ShouldNotBeNull();
        bundle.MutableNode["entry"].ShouldNotBeNull();

        var entries = bundle.MutableNode["entry"]?.AsArray();
        entries!.Count.ShouldBeGreaterThanOrEqualTo(2);

        // First entry should be the patient
        var firstResource = entries[0]?["resource"];
        firstResource?["resourceType"]?.GetValue<string>().ShouldBe("Patient");
    }

    #endregion
}
