// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Scenarios;

public class ScenarioBuilderSeedTests
{
    [Fact]
    public void GivenSameSeed_WhenBuildingTwice_ThenPatientDemographicsMatch()
    {
        var schemaProvider = new R4CoreSchemaProvider();

        var first = new ScenarioBuilder(schemaProvider, 42).WithPatient(p => p.WithAge(50)).Build();
        var second = new ScenarioBuilder(schemaProvider, 42).WithPatient(p => p.WithAge(50)).Build();

        first.Patient!.MutableNode["birthDate"]!.ToString().ShouldBe(second.Patient!.MutableNode["birthDate"]!.ToString());
    }

    [Fact]
    public void GivenDifferentSeeds_WhenBuilding_ThenAtLeastOneFieldDiffers()
    {
        var schemaProvider = new R4CoreSchemaProvider();

        var first = new ScenarioBuilder(schemaProvider, 1).WithPatient().Build();
        var second = new ScenarioBuilder(schemaProvider, 2).WithPatient().Build();

        var firstName = first.Patient!.MutableNode["name"]!.ToJsonString();
        var secondName = second.Patient!.MutableNode["name"]!.ToJsonString();
        (firstName != secondName || first.Patient!.MutableNode["gender"]!.ToString() != second.Patient!.MutableNode["gender"]!.ToString())
            .ShouldBeTrue("expected at least the PatientBuilder-driven fields to differ across seeds");
    }
}
