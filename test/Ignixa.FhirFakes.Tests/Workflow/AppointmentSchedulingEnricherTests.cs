// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.FhirFakes.Workflow;
using Ignixa.FhirFakes.Workflow.Enrichers;
using Ignixa.Specification.Generated;
using Shouldly;
using Ignixa.Serialization.TestSupport;

namespace Ignixa.FhirFakes.Tests.Workflow;

public class AppointmentSchedulingEnricherTests
{
    [Fact]
    public void GivenPractitionersAndSubjects_WhenEnriching_ThenAppointmentsLinkPatientAndPractitioner()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 1);
        var practitionerContext = new ScenarioContext();
        PractitionerState.FamilyPractitioner().Execute(practitionerContext, faker);
        var practitioner = practitionerContext.CurrentPractitioner!;

        var patientContext = new ScenarioBuilder(schemaProvider, 2).WithPatient().AddState(EncounterState.Ambulatory()).Build();
        var graph = new ResourceGraph();
        graph.AddScenario(practitionerContext);
        graph.AddScenario(patientContext);

        var enricher = new AppointmentSchedulingEnricher(
            [practitioner],
            [(patientContext.Patient!, patientContext.CurrentEncounter!)],
            new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero));

        enricher.Enrich(graph, new ResourceGraphEnrichmentContext
        {
            SchemaProvider = schemaProvider,
            Faker = faker,
            Clock = TimeProvider.System,
        });

        var appointment = graph.AllResources.Single(r => r.ResourceType == "Appointment");
        var participants = appointment.MutableNode()["participant"]!.AsArray();
        participants.Any(p => p!["actor"]!["reference"]!.ToString() == $"Patient/{patientContext.Patient!.Id}").ShouldBeTrue();
        participants.Any(p => p!["actor"]!["reference"]!.ToString() == $"Practitioner/{practitioner.Id}").ShouldBeTrue();
    }

    [Fact]
    public void GivenAppointmentCreated_WhenEnriching_ThenEncounterBackReferencesAppointment()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 1);
        var practitionerContext = new ScenarioContext();
        PractitionerState.FamilyPractitioner().Execute(practitionerContext, faker);
        var patientContext = new ScenarioBuilder(schemaProvider, 2).WithPatient().AddState(EncounterState.Ambulatory()).Build();
        var graph = new ResourceGraph();
        graph.AddScenario(practitionerContext);
        graph.AddScenario(patientContext);

        var enricher = new AppointmentSchedulingEnricher(
            [practitionerContext.CurrentPractitioner!],
            [(patientContext.Patient!, patientContext.CurrentEncounter!)],
            new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero));

        enricher.Enrich(graph, new ResourceGraphEnrichmentContext { SchemaProvider = schemaProvider, Faker = faker, Clock = TimeProvider.System });

        var appointment = graph.AllResources.Single(r => r.ResourceType == "Appointment");
        patientContext.CurrentEncounter!.MutableNode()["appointment"]!.AsArray().Single()!["reference"]!.ToString().ShouldBe($"Appointment/{appointment.Id}");
    }

    [Fact]
    public void GivenStu3Provider_WhenEnriching_ThenEncounterAppointmentIsScalar()
    {
        var appointmentNode = EnrichAndGetEncounterAppointment(new STU3CoreSchemaProvider());

        appointmentNode.ShouldBeOfType<JsonObject>();
        appointmentNode!["reference"]!.ToString().ShouldStartWith("Appointment/");
    }

    [Fact]
    public void GivenR4Provider_WhenEnriching_ThenEncounterAppointmentIsArray()
    {
        var appointmentNode = EnrichAndGetEncounterAppointment(new R4CoreSchemaProvider());

        appointmentNode.ShouldBeOfType<JsonArray>();
        appointmentNode!.AsArray().Single()!["reference"]!.ToString().ShouldStartWith("Appointment/");
    }

    [Fact]
    public void GivenNoPractitioners_WhenConstructing_ThenThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            new AppointmentSchedulingEnricher([], [], DateTimeOffset.UtcNow));
    }

    private static JsonNode? EnrichAndGetEncounterAppointment(IFhirSchemaProvider schemaProvider)
    {
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 1);
        var practitionerContext = new ScenarioContext();
        PractitionerState.FamilyPractitioner().Execute(practitionerContext, faker);
        var patientContext = new ScenarioBuilder(schemaProvider, 2).WithPatient().AddState(EncounterState.Ambulatory()).Build();
        var graph = new ResourceGraph();
        graph.AddScenario(practitionerContext);
        graph.AddScenario(patientContext);

        var enricher = new AppointmentSchedulingEnricher(
            [practitionerContext.CurrentPractitioner!],
            [(patientContext.Patient!, patientContext.CurrentEncounter!)],
            new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero));

        enricher.Enrich(graph, new ResourceGraphEnrichmentContext
        {
            SchemaProvider = schemaProvider,
            Faker = faker,
            Clock = TimeProvider.System,
        });

        return patientContext.CurrentEncounter!.MutableNode()["appointment"];
    }
}
