// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Builders.Profiles;
using Ignixa.FhirFakes.Population;
using Ignixa.Abstractions;
using Ignixa.Specification.Generated;
using Xunit;

namespace Ignixa.FhirFakes.Tests.Builders;

/// <summary>
/// Determinism tests for the seeded PatientBuilder generation path.
/// </summary>
/// <remarks>
/// Verifies the seeding contract: given the same seed and the same explicit configuration,
/// <see cref="PatientBuilder.Build"/> produces byte-identical JSON except for the
/// server-managed <c>meta.lastUpdated</c> value.
/// </remarks>
public class PatientBuilderDeterminismTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    [Fact]
    public void GivenSameSeedAndConfig_WhenBuildingTwice_ThenOutputsAreByteIdenticalExceptLastUpdated()
    {
        // Arrange & Act
        var first = BuildConfiguredPatient(seed: 1234);
        var second = BuildConfiguredPatient(seed: 1234);

        // Assert
        IdOf(first).ShouldBe(IdOf(second));
        Canonicalize(first).ShouldBe(Canonicalize(second));
    }

    [Fact]
    public void GivenDifferentSeeds_WhenBuilding_ThenOutputsDiffer()
    {
        // Arrange & Act
        var first = BuildConfiguredPatient(seed: 1);
        var second = BuildConfiguredPatient(seed: 2);

        // Assert
        Canonicalize(first).ShouldNotBe(Canonicalize(second));
    }

    [Fact]
    public void GivenSameSeed_WhenBuildingFromCity_ThenOutputsAreByteIdentical()
    {
        // Arrange & Act
        var first = PatientBuilderFactory.Create(_schemaProvider, 1234)
            .FromCity(KnownCities.Boston).Build().MutableNode;
        var second = PatientBuilderFactory.Create(_schemaProvider, 1234)
            .FromCity(KnownCities.Boston).Build().MutableNode;

        // Assert
        Canonicalize(first).ShouldBe(Canonicalize(second));
    }

    [Fact]
    public void GivenSameSeed_WhenBuildingFromSeattle_ThenOutputsAreByteIdentical()
    {
        // Arrange & Act
        var first = PatientBuilderFactory.Create(_schemaProvider, 1234)
            .FromSeattle().Build().MutableNode;
        var second = PatientBuilderFactory.Create(_schemaProvider, 1234)
            .FromSeattle().Build().MutableNode;

        // Assert
        Canonicalize(first).ShouldBe(Canonicalize(second));
    }

    [Fact]
    public void GivenDifferentSeeds_WhenBuildingFromCity_ThenOutputsDiffer()
    {
        // Arrange & Act
        var first = PatientBuilderFactory.Create(_schemaProvider, 1)
            .FromCity(KnownCities.Boston).Build().MutableNode;
        var second = PatientBuilderFactory.Create(_schemaProvider, 2)
            .FromCity(KnownCities.Boston).Build().MutableNode;

        // Assert
        Canonicalize(first).ShouldNotBe(Canonicalize(second));
    }

    [Fact]
    public void GivenSameSeed_WhenBuildingUsCoreProfilePatient_ThenRaceContentReproduces()
    {
        // Arrange & Act
        var first = BuildUsCoreProfilePatient(seed: 9090);
        var second = BuildUsCoreProfilePatient(seed: 9090);

        // Assert
        var firstRace = RaceText(first);
        var secondRace = RaceText(second);
        firstRace.ShouldNotBeNullOrEmpty();
        firstRace.ShouldBe(secondRace);
        Canonicalize(first).ShouldBe(Canonicalize(second));
    }

    [Fact]
    public void GivenUnseededFactory_WhenBuilding_ThenStillProducesPatient()
    {
        // Arrange & Act
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(40)
            .WithGender(g => g.Female)
            .WithGivenName("Ada")
            .WithFamilyName("Lovelace")
            .Build();

        // Assert
        patient.ShouldNotBeNull();
        patient.ResourceType.ShouldBe("Patient");
        IdOf(patient.MutableNode).ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void GivenSameSeedAndNoExplicitNames_WhenBuildingTwice_ThenNamesAreIdentical()
    {
        // Arrange & Act
        var first = BuildAutoNamedPatient(seed: 7777);
        var second = BuildAutoNamedPatient(seed: 7777);

        // Assert
        Canonicalize(first).ShouldBe(Canonicalize(second));
    }

    private JsonNode BuildAutoNamedPatient(int seed) =>
        PatientBuilderFactory.Create(_schemaProvider, seed)
            .WithAge(35)
            .WithGender(g => g.Female)
            .Build()
            .MutableNode;

    private JsonNode BuildConfiguredPatient(int seed) =>
        PatientBuilderFactory.Create(_schemaProvider, seed)
            .WithAge(45)
            .WithGender(g => g.Male)
            .WithGivenName("John")
            .WithFamilyName("Smith")
            .WithCity("Boston")
            .WithState("Massachusetts")
            .WithZipCode("02101")
            .WithAreaCode("617")
            .WithRealisticBMI()
            .Build()
            .MutableNode;

    private JsonNode BuildUsCoreProfilePatient(int seed) =>
        PatientBuilderFactory.Create(_schemaProvider, seed)
            .WithAge(32)
            .WithGender(g => g.Female)
            .WithGivenName("Grace")
            .WithFamilyName("Hopper")
            .WithProfile(USCorePatientProfile.Instance)
            .WithAttribute(USCorePatientProfile.UsCoreRaceAttribute, USCorePatientProfile.Race.Hispanic)
            .Build()
            .MutableNode;

    private static string IdOf(JsonNode patient) =>
        patient["id"]?.GetValue<string>() ?? string.Empty;

    private static string? RaceText(JsonNode patient)
    {
        var race = (patient["extension"] as JsonArray)?
            .OfType<JsonObject>()
            .FirstOrDefault(e => e["url"]?.GetValue<string>()?.EndsWith("us-core-race", StringComparison.Ordinal) == true);
        return (race?["extension"] as JsonArray)?
            .OfType<JsonObject>()
            .FirstOrDefault()?["valueString"]?.GetValue<string>();
    }

    private static string Canonicalize(JsonNode patient)
    {
        var clone = JsonNode.Parse(patient.ToJsonString())!.AsObject();
        if (clone["meta"] is JsonObject meta)
        {
            meta.Remove("lastUpdated");
        }

        return clone.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
