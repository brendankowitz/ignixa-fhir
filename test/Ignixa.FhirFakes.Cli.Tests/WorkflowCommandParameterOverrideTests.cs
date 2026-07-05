// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Cli.Commands;
using Ignixa.FhirFakes.Workflow;
using Shouldly;

namespace Ignixa.FhirFakes.Cli.Tests;

public class WorkflowCommandParameterOverrideTests
{
    [Fact]
    public void GivenValidParamValues_WhenParsing_ThenOverridesContainConvertedValues()
    {
        var scenario = WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")!;

        var success = ScenarioCommand.TryParseParameterOverrides(
            scenario.Id, scenario.Parameters, ["appointmentCount=4", "practitionerCount=2"], out var overrides, out var error);

        success.ShouldBeTrue();
        error.ShouldBeNull();
        overrides["appointmentCount"].ShouldBe(4);
        overrides["practitionerCount"].ShouldBe(2);
    }

    [Fact]
    public void GivenOutOfRangeValue_WhenParsing_ThenFails()
    {
        var scenario = WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")!;

        var success = ScenarioCommand.TryParseParameterOverrides(
            scenario.Id, scenario.Parameters, ["practitionerCount=99"], out _, out var error);

        success.ShouldBeFalse();
        error.ShouldNotBeNull();
    }
}
