// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Cli.Commands;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Cli.Tests;

/// <summary>
/// Covers the <c>scenario</c>/<c>workflow</c> <c>--validate</c> exit-code fix: both commands
/// previously computed an invalid-resource count but never set <see cref="Environment.ExitCode"/>,
/// so a caller scripting around a validation failure could not detect it. Constructing a genuinely
/// invalid resource through the full scenario/workflow generation pipeline is impractical — every
/// catalog scenario passes schema validation on every supported FHIR version (see
/// ScenarioCatalogCrossVersionValidationTests) — so this exercises the extracted summary/exit-code
/// logic (<see cref="ScenarioCommand.ReportValidationSummary"/>) directly, and separately proves a
/// real end-to-end run of both commands still exits 0 when nothing is invalid.
/// </summary>
public class ValidationSummaryExitCodeTests
{
    [Fact]
    public void GivenInvalidResourcesPresent_WhenReportingValidationSummary_ThenSetsExitCode1()
    {
        Environment.ExitCode = 0;

        ScenarioCommand.ReportValidationSummary(invalidCount: 2, totalCount: 5);

        Environment.ExitCode.ShouldBe(1);
    }

    [Fact]
    public void GivenNoInvalidResources_WhenReportingValidationSummary_ThenExitCodeIsUnchanged()
    {
        Environment.ExitCode = 0;

        ScenarioCommand.ReportValidationSummary(invalidCount: 0, totalCount: 5);

        Environment.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task GivenValidScenario_WhenRunningScenarioCommandWithValidate_ThenExitsSuccessfully()
    {
        Environment.ExitCode = 0;
        var tempDir = CreateTempDirectory();

        try
        {
            var schemaProvider = new R4CoreSchemaProvider();
            var command = ScenarioCommand.Create(schemaProvider, "r4");
            var args = new[] { "DiabeticPatient", "--out", tempDir, "--validate" };

            var parseResult = command.Parse(args);
            await parseResult.InvokeAsync();

            Environment.ExitCode.ShouldBe(0);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [Fact]
    public async Task GivenValidWorkflowScenario_WhenRunningWorkflowCommandWithValidate_ThenExitsSuccessfully()
    {
        Environment.ExitCode = 0;
        var tempDir = CreateTempDirectory();

        try
        {
            var schemaProvider = new R4CoreSchemaProvider();
            var command = WorkflowCommand.Create(schemaProvider, "r4");
            var args = new[] { "DailyAppointmentSchedule", "--out", tempDir, "--validate" };

            var parseResult = command.Parse(args);
            await parseResult.InvokeAsync();

            Environment.ExitCode.ShouldBe(0);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "fhir-fakes-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void CleanupTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
