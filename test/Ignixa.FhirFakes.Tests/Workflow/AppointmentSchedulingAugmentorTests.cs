// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.FhirFakes.Workflow;
using Ignixa.FhirFakes.Workflow.Augmentors;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Workflow;

public class AppointmentSchedulingAugmentorTests
{
    [Fact]
    public void GivenPractitionersAndSubjects_WhenAugmenting_ThenAppointmentsLinkPatientAndPractitioner()
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

        var augmentor = new AppointmentSchedulingAugmentor(
            [practitioner],
            [(patientContext.Patient!, patientContext.CurrentEncounter!)],
            new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero));

        augmentor.Augment(graph, new ResourceGraphAugmentationContext
        {
            SchemaProvider = schemaProvider,
            Faker = faker,
            Clock = TimeProvider.System,
        });

        var appointment = graph.AllResources.Single(r => r.ResourceType == "Appointment");
        var participants = appointment.MutableNode["participant"]!.AsArray();
        participants.Any(p => p!["actor"]!["reference"]!.ToString() == $"Patient/{patientContext.Patient!.Id}").ShouldBeTrue();
        participants.Any(p => p!["actor"]!["reference"]!.ToString() == $"Practitioner/{practitioner.Id}").ShouldBeTrue();
    }

    [Fact]
    public void GivenAppointmentCreated_WhenAugmenting_ThenEncounterBackReferencesAppointment()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 1);
        var practitionerContext = new ScenarioContext();
        PractitionerState.FamilyPractitioner().Execute(practitionerContext, faker);
        var patientContext = new ScenarioBuilder(schemaProvider, 2).WithPatient().AddState(EncounterState.Ambulatory()).Build();
        var graph = new ResourceGraph();
        graph.AddScenario(practitionerContext);
        graph.AddScenario(patientContext);

        var augmentor = new AppointmentSchedulingAugmentor(
            [practitionerContext.CurrentPractitioner!],
            [(patientContext.Patient!, patientContext.CurrentEncounter!)],
            new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero));

        augmentor.Augment(graph, new ResourceGraphAugmentationContext { SchemaProvider = schemaProvider, Faker = faker, Clock = TimeProvider.System });

        var appointment = graph.AllResources.Single(r => r.ResourceType == "Appointment");
        patientContext.CurrentEncounter!.MutableNode["appointment"]!["reference"]!.ToString().ShouldBe($"Appointment/{appointment.Id}");
    }

    [Fact]
    public void GivenNoPractitioners_WhenConstructing_ThenThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            new AppointmentSchedulingAugmentor([], [], DateTimeOffset.UtcNow));
    }
}
