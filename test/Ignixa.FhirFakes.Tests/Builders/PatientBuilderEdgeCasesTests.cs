// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Builders;
using Ignixa.Specification.Generated;
using Shouldly;
using Xunit;

namespace Ignixa.FhirFakes.Tests.Builders;

/// <summary>
/// Tests for <see cref="PatientBuilder.WithEdgeCases"/> integration.
/// </summary>
public class PatientBuilderEdgeCasesTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    [Fact]
    public void GivenSeededBuilder_WhenWithEdgeCases_ThenManifestPopulatedWithMutations()
    {
        var builder = PatientBuilderFactory.Create(_schemaProvider, seed: 42)
            .WithGivenName("Jane")
            .WithFamilyName("Doe")
            .WithAge(35)
            .WithEdgeCases();

        var patient = builder.Build();

        builder.LastEdgeCaseManifest.ShouldNotBeNull();
        builder.LastEdgeCaseManifest.Mutations.Count.ShouldBeGreaterThanOrEqualTo(1);
        patient.ResourceType.ShouldBe("Patient");
        patient.MutableNode["name"].ShouldNotBeNull();
        patient.MutableNode["birthDate"].ShouldNotBeNull();
    }

    [Fact]
    public void GivenSameSeedAndConfig_WhenWithEdgeCasesBuiltTwice_ThenOutputsAndManifestsAreIdentical()
    {
        var first = BuildEdgeCasePatient(seed: 99);
        var second = BuildEdgeCasePatient(seed: 99);

        Canonicalize(first.resource).ShouldBe(Canonicalize(second.resource));
        first.manifest.Seed.ShouldBe(second.manifest.Seed);
        first.manifest.Mutations.Count.ShouldBe(second.manifest.Mutations.Count);

        for (var i = 0; i < first.manifest.Mutations.Count; i++)
        {
            first.manifest.Mutations[i].Category.ShouldBe(second.manifest.Mutations[i].Category);
            first.manifest.Mutations[i].Path.ShouldBe(second.manifest.Mutations[i].Path);
            first.manifest.Mutations[i].After.ShouldBe(second.manifest.Mutations[i].After);
        }
        first.manifest.Mutations.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void GivenTemporalSelector_WhenWithEdgeCases_ThenOnlyTemporalMutationsApplied()
    {
        var builder = PatientBuilderFactory.Create(_schemaProvider, seed: 55)
            .WithGivenName("Test")
            .WithFamilyName("Patient")
            .WithAge(40)
            .WithEdgeCases(selectors: ["temporal"]);

        builder.Build();

        builder.LastEdgeCaseManifest.ShouldNotBeNull();
        builder.LastEdgeCaseManifest.Mutations.ShouldAllBe(m => m.Category.StartsWith("temporal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GivenNoWithEdgeCases_WhenBuilding_ThenManifestIsNull()
    {
        var builder = PatientBuilderFactory.Create(_schemaProvider, seed: 7)
            .WithGivenName("No")
            .WithFamilyName("EdgeCases")
            .WithAge(30);

        builder.Build();

        builder.LastEdgeCaseManifest.ShouldBeNull();
    }

    private (Ignixa.Serialization.SourceNodes.ResourceJsonNode resource, Ignixa.FhirFakes.EdgeCases.MutationManifest manifest) BuildEdgeCasePatient(int seed)
    {
        var builder = PatientBuilderFactory.Create(_schemaProvider, seed: seed)
            .WithGivenName("John")
            .WithFamilyName("Smith")
            .WithAge(45)
            .WithEdgeCases();

        var resource = builder.Build();
        return (resource, builder.LastEdgeCaseManifest!);
    }

    private static string Canonicalize(Ignixa.Serialization.SourceNodes.ResourceJsonNode resource)
    {
        var clone = JsonNode.Parse(resource.MutableNode.ToJsonString())!.AsObject();
        if (clone["meta"] is JsonObject meta)
        {
            meta.Remove("lastUpdated");
        }
        return clone.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
