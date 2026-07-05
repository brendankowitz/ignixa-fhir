// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.FhirFakes.Workflow.Enrichers;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.Workflow.Predefined;

/// <summary>
/// Built-in workflow scenario pack: a practitioner's daily appointment schedule, with each
/// appointment linking a Patient, Practitioner, and Encounter through a search-response-ready graph.
/// </summary>
public static class DailyAppointmentScheduleScenario
{
    private static readonly Func<PractitionerState>[] PractitionerRoster =
    [
        PractitionerState.FamilyPractitioner,
        PractitionerState.Internist,
        PractitionerState.Pediatrician,
    ];

    [Scenario(
        Id = "DailyAppointmentSchedule",
        Category = "Schedule",
        Description = "Practitioner day schedule with appointments linking patient, practitioner, and encounter context")]
    public static WorkflowScenarioResult GetDailyAppointmentSchedule(
        IFhirSchemaProvider schemaProvider,
        WorkflowScenarioOptions options,
        [ScenarioParameter(Min = 1, Max = 10, Description = "Number of practitioners on the schedule")] int practitionerCount = 1,
        [ScenarioParameter(Min = 0, Max = 50, Description = "Number of appointments across all practitioners")] int appointmentCount = 12)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(options);

        var faker = options.Seed is int seed
            ? new SchemaBasedFhirResourceFaker(schemaProvider, seed)
            : new SchemaBasedFhirResourceFaker(schemaProvider);
        if (options.Tag is not null)
        {
            faker.WithTag(options.Tag);
        }

        var graph = new ResourceGraph();

        var practitioners = new List<ResourceJsonNode>(practitionerCount);
        for (var i = 0; i < practitionerCount; i++)
        {
            var carrier = new ScenarioContext();
            PractitionerRoster[i % PractitionerRoster.Length]().Execute(carrier, faker);
            graph.AddScenario(carrier);
            practitioners.Add(carrier.CurrentPractitioner!);
        }

        var appointmentSubjects = new List<(ResourceJsonNode Patient, ResourceJsonNode Encounter)>(appointmentCount);
        for (var i = 0; i < appointmentCount; i++)
        {
            var patientScenario = options.Seed is int baseSeed
                ? new ScenarioBuilder(schemaProvider, baseSeed + i + 1)
                : new ScenarioBuilder(schemaProvider);

            var context = patientScenario
                .WithTag(options.Tag)
                .WithPatient(_ => { })
                .AddState(EncounterState.Ambulatory("Scheduled visit"))
                .Build();

            graph.AddScenario(context);
            appointmentSubjects.Add((context.Patient!, context.CurrentEncounter!));
        }

        if (appointmentSubjects.Count > 0)
        {
            var scheduleDate = new DateTimeOffset(options.Clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
            var enricher = new AppointmentSchedulingEnricher(practitioners, appointmentSubjects, scheduleDate);
            enricher.Enrich(graph, new ResourceGraphEnrichmentContext
            {
                SchemaProvider = schemaProvider,
                Faker = faker,
                Clock = options.Clock,
            });
        }

        var resourceCounts = graph.AllResources
            .GroupBy(r => r.ResourceType)
            .ToDictionary(g => g.Key, g => g.Count());

        return new WorkflowScenarioResult
        {
            Graph = graph,
            Manifest = new WorkflowManifest
            {
                ScenarioId = "DailyAppointmentSchedule",
                Seed = options.Seed,
                PrimaryResourceType = "Appointment",
                ResourceCountsByType = resourceCounts,
            },
        };
    }
}
