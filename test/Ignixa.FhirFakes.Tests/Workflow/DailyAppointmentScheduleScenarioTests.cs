// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Workflow;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Workflow;

public class DailyAppointmentScheduleScenarioTests
{
    [Fact]
    public void GivenDefaults_WhenInvokedViaCatalog_ThenProducesOneAppointmentPerDefaultCount()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")!;

        var result = WorkflowScenarioCatalog.Invoke(scenario, schemaProvider, new WorkflowScenarioOptions { Seed = 10 });

        result.Manifest.ResourceCountsByType["Appointment"].ShouldBe(12);
        result.Manifest.ResourceCountsByType["Patient"].ShouldBe(12);
        result.Manifest.ResourceCountsByType["Practitioner"].ShouldBe(1);
        result.Manifest.PrimaryResourceType.ShouldBe("Appointment");
    }

    [Fact]
    public void GivenParameterOverrides_WhenInvoked_ThenCountsMatchOverrides()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")!;

        var result = WorkflowScenarioCatalog.Invoke(
            scenario, schemaProvider, new WorkflowScenarioOptions { Seed = 10 },
            new Dictionary<string, object?> { ["practitionerCount"] = 2, ["appointmentCount"] = 4 });

        result.Manifest.ResourceCountsByType["Practitioner"].ShouldBe(2);
        result.Manifest.ResourceCountsByType["Appointment"].ShouldBe(4);
    }

    [Fact]
    public void GivenAppointmentCountZero_WhenInvoked_ThenGraphStillHasPractitionerOnly()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")!;

        var result = WorkflowScenarioCatalog.Invoke(
            scenario, schemaProvider, new WorkflowScenarioOptions(),
            new Dictionary<string, object?> { ["appointmentCount"] = 0 });

        result.Manifest.ResourceCountsByType.ContainsKey("Appointment").ShouldBeFalse();
        result.Manifest.ResourceCountsByType["Practitioner"].ShouldBe(1);
    }

    [Fact]
    public void GivenSameSeed_WhenInvokedTwice_ThenAppointmentStatusRotationMatches()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")!;

        var first = WorkflowScenarioCatalog.Invoke(scenario, schemaProvider, new WorkflowScenarioOptions { Seed = 99 });
        var second = WorkflowScenarioCatalog.Invoke(scenario, schemaProvider, new WorkflowScenarioOptions { Seed = 99 });

        var firstStatuses = first.Graph.AllResources.Where(r => r.ResourceType == "Appointment").Select(r => r.MutableNode["status"]!.ToString()).ToList();
        var secondStatuses = second.Graph.AllResources.Where(r => r.ResourceType == "Appointment").Select(r => r.MutableNode["status"]!.ToString()).ToList();
        firstStatuses.ShouldBe(secondStatuses);
    }
}
