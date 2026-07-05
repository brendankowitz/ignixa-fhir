// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using System.CommandLine;
using System.Text.Json;
using Ignixa.FhirFakes.Workflow;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Cli.Commands;

/// <summary>
/// Command for generating predefined FHIR workflow scenario packs (searchset-shaped fixture data).
/// </summary>
internal static class WorkflowCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var workflowCommand = new Command("workflow", "Generate a predefined FHIR workflow scenario pack");

        var scenarioNameArg = new Argument<string>("scenarioName")
        {
            Description = "The workflow scenario pack name (e.g., DailyAppointmentSchedule)"
        };

        var outOption = new Option<string>("--out")
        {
            Description = "Output folder for generated files",
            Required = true
        };

        var seedOption = new Option<int?>("--seed")
        {
            Description = "Seed for reproducible generation"
        };

        var tagOption = new Option<string?>("--tag")
        {
            Description = "Tag code applied to generated resources, for test isolation via the _tag search parameter"
        };

        var pageSizeOption = new Option<int>("--page-size")
        {
            Description = "Maximum matching entries per composed page",
            DefaultValueFactory = _ => 20
        };

        var validateOption = new Option<bool>("--validate")
        {
            Description = "Validate generated resources against schema", DefaultValueFactory = _ => false
        };

        var paramOption = new Option<string[]>("--param")
        {
            Description = "Override a workflow parameter, format name=value (repeatable, e.g. --param appointmentCount=20)",
            DefaultValueFactory = _ => []
        };

        workflowCommand.Arguments.Add(scenarioNameArg);
        workflowCommand.Options.Add(outOption);
        workflowCommand.Options.Add(seedOption);
        workflowCommand.Options.Add(tagOption);
        workflowCommand.Options.Add(pageSizeOption);
        workflowCommand.Options.Add(validateOption);
        workflowCommand.Options.Add(paramOption);

        workflowCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var scenarioName = parseResult.GetValue(scenarioNameArg)!;
            var outFolder = parseResult.GetValue(outOption)!;
            var seed = parseResult.GetValue(seedOption);
            var tag = parseResult.GetValue(tagOption);
            var pageSize = parseResult.GetValue(pageSizeOption);
            var validate = parseResult.GetValue(validateOption);
            var paramValues = parseResult.GetValue(paramOption) ?? [];

            await HandleWorkflowCommand(schemaProvider, fhirVersion, scenarioName, outFolder, seed, tag, pageSize, validate, paramValues, cancellationToken);
        });

        return workflowCommand;
    }

    private static async Task HandleWorkflowCommand(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string scenarioName,
        string outFolder,
        int? seed,
        string? tag,
        int pageSize,
        bool validate,
        string[] paramValues,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(outFolder);

            var scenario = WorkflowScenarioCatalog.Find(scenarioName);
            if (scenario == null)
            {
                Console.WriteLine($"X Unknown workflow scenario: {scenarioName}");
                Console.WriteLine("Available workflow scenarios:");
                foreach (var name in WorkflowScenarioCatalog.GetAll().Select(s => s.Id).OrderBy(s => s))
                {
                    Console.WriteLine($"  - {name}");
                }
                Environment.ExitCode = 2;
                return;
            }

            if (!ScenarioCommand.TryParseParameterOverrides(scenario.Id, scenario.Parameters, paramValues, out var overrides, out var parseError))
            {
                Console.WriteLine($"X {parseError}");
                Environment.ExitCode = 2;
                return;
            }

            if (pageSize <= 0)
            {
                Console.WriteLine($"X --page-size must be greater than zero, but was {pageSize}");
                Environment.ExitCode = 2;
                return;
            }

            var options = new WorkflowScenarioOptions { Seed = seed, Tag = tag };

            Ignixa.FhirFakes.Workflow.WorkflowScenarioResult result;
            try
            {
                result = WorkflowScenarioCatalog.Invoke(scenario, schemaProvider, options, overrides);
            }
            catch (Ignixa.FhirFakes.Scenarios.ScenarioInvocationException ex)
            {
                Console.WriteLine($"X Error: {ex.Message}");
                Environment.ExitCode = 1;
                return;
            }

            var composer = new SearchsetBundleComposer();
            var searchUrl = $"/{result.Manifest.PrimaryResourceType}";
            var pages = composer.Compose(result.Graph, new SearchResponseOptions
            {
                SearchUrl = searchUrl,
                MatchResourceType = result.Manifest.PrimaryResourceType,
                PageSize = pageSize,
            });

            var runId = Guid.NewGuid().ToString();
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

            for (var i = 0; i < pages.Count; i++)
            {
                var filename = $"{fhirVersion}-workflow-{scenario.Id}-{runId}-page{i}.json";
                var outputPath = Path.Combine(outFolder, filename);
                var json = JsonSerializer.Serialize(pages[i].MutableNode, jsonOptions);
                await File.WriteAllTextAsync(outputPath, json, cancellationToken);
                Console.WriteLine($"Generated workflow page {i + 1}/{pages.Count}: {outputPath}");
            }

            var manifestPath = Path.Combine(outFolder, $"{fhirVersion}-workflow-{scenario.Id}-{runId}-manifest.json");
            var manifestJson = JsonSerializer.Serialize(new
            {
                result.Manifest.ScenarioId,
                result.Manifest.Seed,
                result.Manifest.PrimaryResourceType,
                result.Manifest.ResourceCountsByType,
                PageCount = pages.Count,
            }, jsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken);
            Console.WriteLine($"Generated manifest: {manifestPath}");

            if (validate)
            {
                Console.WriteLine("\n-------------------------------------------------------------------");
                Console.WriteLine("Validating generated resources...");
                Console.WriteLine("-------------------------------------------------------------------");

                var invalidCount = 0;
                foreach (var resource in result.Graph.AllResources)
                {
                    var resourceType = resource.MutableNode["resourceType"]?.ToString() ?? "Unknown";
                    var resourceId = resource.MutableNode["id"]?.ToString() ?? "unknown";
                    var validationResult = ValidationHelper.ValidateResource(resource.MutableNode, schemaProvider);
                    if (!validationResult.IsValid)
                    {
                        invalidCount++;
                    }
                    Console.WriteLine($"  {resourceType}/{resourceId}: {ValidationHelper.GetSummary(validationResult)}");
                }

                Console.WriteLine(invalidCount > 0
                    ? $"\n  {invalidCount} resource(s) have validation issues"
                    : $"\n  All {result.Graph.AllResources.Count} resource(s) passed validation");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"X Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}
