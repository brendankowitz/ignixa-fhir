// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Bogus;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.EdgeCases;

public class EdgeCaseEndToEndTests
{
    [Fact]
    public void GivenGeneratedPatient_WhenAllStrategiesApplied_ThenRequiredFieldsRemainAndMutationsRecorded()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider);
        var patient = faker.CreatePatient(p => p.WithGivenName("John").WithFamilyName("Smith"));
        var strategies = EdgeCaseCatalog.CreateDefault().All();

        var manifest = new EdgeCasePipeline(12345, schemaProvider).Apply(patient, strategies);

        patient.MutableNode["gender"].ShouldNotBeNull();
        patient.MutableNode["birthDate"].ShouldNotBeNull();
        patient.MutableNode["name"].ShouldNotBeNull();
        manifest.Mutations.Count.ShouldBeGreaterThanOrEqualTo(1);
        manifest.ResourceId.ShouldBe(patient.Id);
        manifest.Seed.ShouldBe(12345);
    }

    [Fact]
    public void GivenCustomStrategyRegistered_WhenResolvingItsCategory_ThenItIsReturned()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();
        catalog.Register(new CustomTitleStrategy());

        var resolved = catalog.Resolve(["custom.title"]);

        resolved.Count.ShouldBe(1);
        resolved[0].Category.ShouldBe("custom.title");
    }

    private sealed class CustomTitleStrategy : IEdgeCaseStrategy
    {
        public string Category => "custom.title";

        public EdgeCaseFamily Family => EdgeCaseFamily.StringBoundary;

        public ValidityIntent Intent => ValidityIntent.PreservesValidity;

        public bool CanApply(MutationTarget target) => target.ElementName == "title";

        public MutationResult Apply(MutationTarget target, Randomizer rng)
            => new("Custom", "custom title mutation");
    }
}
