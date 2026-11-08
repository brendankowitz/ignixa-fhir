// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Ignixa.Abstractions;
using Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;
using Ignixa.Api.E2ETests._TestData.Scenarios;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.FhirFakes;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

/// <summary>
/// The data the SQL Server E2E suite writes, from its scenarios (compiled from the E2E project), the FhirFakes builders and
/// faker its tests call, through the registered mediator and validation pipeline at the tenant's default (Spec) depth. A
/// validator change that rejects a fixture fails here, every rejection at once with its rule and path, rather than in the
/// E2E run one transaction at a time. The FhirFakes catalogue is validated across versions by Ignixa.FhirFakes.Tests.
/// </summary>
public class E2EFixtureWriteTests
{
    // Resource types the E2E tests generate with SchemaBasedFhirResourceFaker and write after adjusting a few fields.
    private static readonly string[] FakedTypes =
    [
        "Observation", "Organization", "Practitioner", "Location", "HealthcareService", "PractitionerRole",
        "OrganizationAffiliation", "Encounter", "Device", "Group", "DiagnosticReport", "Patient",
    ];

    public static TheoryData<string> Sources() =>
    [
        "chaining", "chaining-deleted-patient", "patient-compartment", "patient-compartment-minimal", "include",
        "token-search", "scenario-builder", "faker",
    ];

    // Regression for the 83cc0c9 E2E failure: the chaining and patient-compartment scenarios' DiagnosticReports carried STU3's
    // performer.actor shape in R4, which Spec rejects as an unknown property of Reference (DiagnosticReport.performer[0].actor).
    // The other sources are guards.
    [Theory]
    [MemberData(nameof(Sources))]
    public async Task GivenTheE2EFixtureData_WhenWrittenAtTheDefaultDepth_ThenEveryResourceIsAccepted(string source)
    {
        using var probe = new ValidationPipelineRegistrationTests.WriteHarness();
        var schema = probe.Container.Resolve<IFhirVersionContext>().GetBaseSchemaProvider(FhirVersion.R4);
        var resources = Build(source, schema, $"e2e-{source}");
        using var harness = new ValidationPipelineRegistrationTests.WriteHarness(
            updatableTypes: [.. resources.Select(resource => resource.ResourceType).Distinct(StringComparer.Ordinal)]);

        var rejections = new List<string>();
        for (var i = 0; i < resources.Count; i++)
        {
            try
            {
                await harness.SendAsync(resources[i].SerializeToString(), id: resources[i].Id ?? $"r{i}");
            }
            catch (ValidationException rejected)
            {
                rejections.AddRange(rejected.ValidationResult.Issues
                    .Where(issue => issue.Severity is Ignixa.Validation.IssueSeverity.Error or Ignixa.Validation.IssueSeverity.Fatal)
                    .Select(issue => $"{resources[i].ResourceType}: {issue.Code} at {issue.Path}"));
            }
        }

        rejections.ShouldBeEmpty();
        harness.Writes.ShouldBe(resources.Count);
    }

    private static IReadOnlyList<ResourceJsonNode> Build(string source, IFhirSchemaProvider schema, string tag) => source switch
    {
        "chaining" => schema.GetChainingSearchScenario(tag).AllResources,
        "chaining-deleted-patient" => DeletedPatient(schema, tag),
        "patient-compartment" => schema.CreateCompartmentScenario(tag).AllResources,
        "patient-compartment-minimal" => schema.CreateMinimalCompartmentScenario(tag).AllResources,
        "include" => schema.GetIncludeSearchScenario(tag).AllResources,
        "token-search" => TokenSearchObservations.Build(schema, tag),
        "scenario-builder" => new ScenarioBuilder(schema)
            .WithResolvedReferences()
            .WithTag(tag)
            .WithPatient(patient => patient.FromSeattle())
            .AddEncounter("Test visit")
            .AddObservation(VitalSigns.HeartRate, 72m, "beats/minute", "/min")
            .Build()
            .AllResources,
        "faker" => [.. FakedTypes.Select(new SchemaBasedFhirResourceFaker(schema, seed: 507).WithTag(tag).Generate)],
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown fixture source."),
    };

    private static List<ResourceJsonNode> DeletedPatient(IFhirSchemaProvider schema, string tag)
    {
        var (patient, careTeam) = schema.GetDeletedPatientCareTeamScenario(tag);
        return [patient, careTeam];
    }
}
