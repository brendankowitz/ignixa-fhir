// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Tests for AllergyIntoleranceState. Tests allergy records with reactions and criticality.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class AllergyIntoleranceStateTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Basic Generation Tests

    [Fact]
    public void GivenAllergy_WhenGenerated_ThenCreatesAllergyIntolerance()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        scenario.Allergies.Should().HaveCount(1);
        var allergy = scenario.Allergies[0];
        allergy.ResourceType.Should().Be("AllergyIntolerance");
        allergy.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenAllergy_WhenGenerated_ThenHasActiveClinicalStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var statusCode = allergy.MutableNode["clinicalStatus"]?["coding"]?[0]?["code"]?.GetValue<string>();
        statusCode.Should().Be("active");
    }

    [Fact]
    public void GivenAllergy_WhenGenerated_ThenHasConfirmedVerificationStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var statusCode = allergy.MutableNode["verificationStatus"]?["coding"]?[0]?["code"]?.GetValue<string>();
        statusCode.Should().Be("confirmed");
    }

    [Fact]
    public void GivenAllergy_WhenGenerated_ThenHasCorrectAllergenCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var code = allergy.MutableNode["code"]?["coding"]?[0]?["code"]?.GetValue<string>();
        code.Should().Be("91935009"); // SNOMED CT for peanut
    }

    #endregion

    #region Patient Reference Tests

    [Fact]
    public void GivenAllergy_WhenGenerated_ThenReferencesPatient()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var patientRef = allergy.MutableNode["patient"]?["reference"]?.GetValue<string>();
        patientRef.Should().Be($"urn:uuid:{scenario.Patient!.Id}");
    }

    [Fact]
    public void GivenAllergy_WhenEncounterExists_ThenReferencesEncounter()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddEncounter("Allergy assessment")
            .AddPeanutAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var encounterRef = allergy.MutableNode["encounter"]?["reference"]?.GetValue<string>();
        encounterRef.Should().Be($"urn:uuid:{scenario.Encounters[0].Id}");
    }

    #endregion

    #region Severity and Criticality Tests

    [Fact]
    public void GivenSevereAllergy_WhenGenerated_ThenHasHighCriticality()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy() // Peanut allergy is severe
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var criticality = allergy.MutableNode["criticality"]?.GetValue<string>();
        criticality.Should().Be("high");
    }

    [Fact]
    public void GivenModerateAllergy_WhenGenerated_ThenHasLowCriticality()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddAllergy(AllergyIntoleranceState.ShellfishAllergy())
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var criticality = allergy.MutableNode["criticality"]?.GetValue<string>();
        criticality.Should().Be("low");
    }

    [Fact]
    public void GivenMildAllergy_WhenGenerated_ThenHasLowCriticality()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddAllergy(AllergyIntoleranceState.GrassPollenAllergy())
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var criticality = allergy.MutableNode["criticality"]?.GetValue<string>();
        criticality.Should().Be("low");
    }

    #endregion

    #region Category Tests

    [Fact]
    public void GivenFoodAllergy_WhenGenerated_ThenHasFoodCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var category = allergy.MutableNode["category"]?[0]?.GetValue<string>();
        category.Should().Be("food");
    }

    [Fact]
    public void GivenMedicationAllergy_WhenGenerated_ThenHasMedicationCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPenicillinAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var category = allergy.MutableNode["category"]?[0]?.GetValue<string>();
        category.Should().Be("medication");
    }

    [Fact]
    public void GivenEnvironmentalAllergy_WhenGenerated_ThenHasEnvironmentCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddAllergy(AllergyIntoleranceState.GrassPollenAllergy())
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var category = allergy.MutableNode["category"]?[0]?.GetValue<string>();
        category.Should().Be("environment");
    }

    [Fact]
    public void GivenBiologicAllergy_WhenGenerated_ThenHasBiologicCategory()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddAllergy(AllergyIntoleranceState.LatexAllergy())
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var category = allergy.MutableNode["category"]?[0]?.GetValue<string>();
        category.Should().Be("biologic");
    }

    [Fact]
    public void GivenCategoryInference_WhenAllergenIsPenicillin_ThenInfersMedication()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddAllergy(Allergens.Penicillin, "moderate") // No explicit category
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var category = allergy.MutableNode["category"]?[0]?.GetValue<string>();
        category.Should().Be("medication");
    }

    #endregion

    #region Reaction Tests

    [Fact]
    public void GivenAllergyWithReactions_WhenGenerated_ThenHasReactions()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy() // Has anaphylaxis, hives, swelling
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var reactions = allergy.MutableNode["reaction"] as System.Text.Json.Nodes.JsonArray;
        reactions.Should().NotBeNull();
        reactions!.Count.Should().Be(3);
    }

    [Fact]
    public void GivenAllergyWithAnaphylaxis_WhenGenerated_ThenHasCorrectManifestationCode()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var reactions = allergy.MutableNode["reaction"] as System.Text.Json.Nodes.JsonArray;
        var anaphylaxisCode = reactions?[0]?["manifestation"]?[0]?["coding"]?[0]?["code"]?.GetValue<string>();
        anaphylaxisCode.Should().Be("39579001"); // SNOMED CT for anaphylaxis
    }

    [Fact]
    public void GivenCustomReactions_WhenGenerated_ThenUsesProvidedReactions()
    {
        // Arrange & Act
        var customReactions = new[] { "Nausea", "Vomiting" };
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddAllergy(Allergens.Shellfish, "moderate", customReactions)
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var reactions = allergy.MutableNode["reaction"] as System.Text.Json.Nodes.JsonArray;
        reactions!.Count.Should().Be(2);
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void GivenPenicillinAllergyFactory_WhenGenerated_ThenHasSeverity()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPenicillinAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var reactions = allergy.MutableNode["reaction"] as System.Text.Json.Nodes.JsonArray;
        var severity = reactions?[0]?["severity"]?.GetValue<string>();
        severity.Should().Be("severe");
    }

    [Fact]
    public void GivenDustMiteAllergyFactory_WhenGenerated_ThenHasMildSeverity()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddAllergy(AllergyIntoleranceState.DustMiteAllergy())
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var reactions = allergy.MutableNode["reaction"] as System.Text.Json.Nodes.JsonArray;
        var severity = reactions?[0]?["severity"]?.GetValue<string>();
        severity.Should().Be("mild");
    }

    [Fact]
    public void GivenDairyAllergyFactory_WhenGenerated_ThenHasIntoleranceType()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddAllergy(AllergyIntoleranceState.DairyAllergy())
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var type = allergy.MutableNode["type"]?.GetValue<string>();
        type.Should().Be("intolerance");
    }

    #endregion

    #region Date Tests

    [Fact]
    public void GivenAllergy_WhenGenerated_ThenHasRecordedDate()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var recordedDate = allergy.MutableNode["recordedDate"]?.GetValue<string>();
        recordedDate.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenAllergy_WhenGenerated_ThenHasOnsetDate()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var onsetDate = allergy.MutableNode["onsetDateTime"]?.GetValue<string>();
        onsetDate.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenAllergy_WhenGenerated_ThenOnsetIsBeforeRecordedDate()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var onsetDate = DateTime.Parse(allergy.MutableNode["onsetDateTime"]!.GetValue<string>()!);
        var recordedDate = DateTime.Parse(allergy.MutableNode["recordedDate"]!.GetValue<string>()!);
        onsetDate.Should().BeBefore(recordedDate);
    }

    #endregion

    #region Timeline and Context Tests

    [Fact]
    public void GivenMultipleAllergies_WhenGenerated_ThenAllAddedToContext()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .AddPenicillinAllergy()
            .AddAllergy(AllergyIntoleranceState.GrassPollenAllergy())
            .Build();

        // Assert
        scenario.Allergies.Should().HaveCount(3);
    }

    [Fact]
    public void GivenAllergy_WhenGenerated_ThenAppearsInTimeline()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        var allergyEvents = scenario.Timeline.Where(e => e.EventType == "AllergyIntolerance").ToList();
        allergyEvents.Should().HaveCount(1);
    }

    [Fact]
    public void GivenAllergy_WhenGenerated_ThenAppearsInAllResources()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        scenario.AllResources.Should().Contain(scenario.Allergies[0]);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void GivenAllergyWithoutEncounter_WhenGenerated_ThenCreatesWithoutEncounterReference()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var encounterRef = allergy.MutableNode["encounter"];
        encounterRef.Should().BeNull();
    }

    [Fact]
    public void GivenAllergy_WhenGenerated_ThenHasRecorder()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var recorder = allergy.MutableNode["recorder"]?["display"]?.GetValue<string>();
        recorder.Should().NotBeNullOrEmpty();
        recorder.Should().Contain("MD");
    }

    [Fact]
    public void GivenAllergyWithDefaultType_WhenGenerated_ThenTypeIsAllergy()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddPeanutAllergy()
            .Build();

        // Assert
        var allergy = scenario.Allergies[0];
        var type = allergy.MutableNode["type"]?.GetValue<string>();
        type.Should().Be("allergy");
    }

    #endregion
}
