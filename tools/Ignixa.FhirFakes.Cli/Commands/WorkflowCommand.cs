// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using System.CommandLine;
using System.Text.Json;
using Ignixa.FhirFakes.Workflow;
using Ignixa.Serialization;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Cli.Commands;

/// <summary>
/// Command for generating predefined FHIR workflow scenario packs as transaction or batch bundles,
/// matching the <c>scenario</c> command's output shape.
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

        var resolvedReferencesOption = new Option<bool>("--resolved-references")
        {
            Description = "Create a batch bundle instead of a transaction bundle"
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
        workflowCommand.Options.Add(resolvedReferencesOption);
        workflowCommand.Options.Add(validateOption);
        workflowCommand.Options.Add(paramOption);

        workflowCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var scenarioName = parseResult.GetValue(scenarioNameArg)!;
            var outFolder = parseResult.GetValue(outOption)!;
            var seed = parseResult.GetValue(seedOption);
            var tag = parseResult.GetValue(tagOption);
            var resolvedReferences = parseResult.GetValue(resolvedReferencesOption);
            var validate = parseResult.GetValue(validateOption);
            var paramValues = parseResult.GetValue(paramOption) ?? [];

            await HandleWorkflowCommand(schemaProvider, fhirVersion, scenarioName, outFolder, seed, tag, resolvedReferences, validate, paramValues, cancellationToken);
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
        bool resolvedReferences,
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

            var runId = Guid.NewGuid().ToString();
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

            var bundle = resolvedReferences
                ? ResourceBundleComposer.ToBatchBundle(result.Graph.AllResources)
                : ResourceBundleComposer.ToTransactionBundle(result.Graph.AllResources);
            var bundleFilename = $"{fhirVersion}-workflow-{scenario.Id}-{runId}.json";
            var bundlePath = Path.Combine(outFolder, bundleFilename);
            var bundleJson = bundle.SerializeToString(pretty: true);
            await File.WriteAllTextAsync(bundlePath, bundleJson, cancellationToken);

            var bundleType = resolvedReferences ? "batch" : "transaction";
            Console.WriteLine($"Generated workflow bundle ({bundleType}): {bundlePath}");
            Console.WriteLine($"  Resources: {result.Graph.AllResources.Count}");

            var manifestPath = Path.Combine(outFolder, $"{fhirVersion}-workflow-{scenario.Id}-{runId}-manifest.json");
            var manifestJson = JsonSerializer.Serialize(new
            {
                result.Manifest.ScenarioId,
                result.Manifest.Seed,
                result.Manifest.PrimaryResourceType,
                result.Manifest.ResourceCountsByType,
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
                    var resourceType = string.IsNullOrEmpty(resource.ResourceType) ? "Unknown" : resource.ResourceType;
                    var resourceId = string.IsNullOrEmpty(resource.Id) ? "unknown" : resource.Id;
                    var validationResult = ValidationHelper.ValidateResource(resource, schemaProvider);
                    if (!validationResult.IsValid)
                    {
                        invalidCount++;
                    }
                    Console.WriteLine($"  {resourceType}/{resourceId}: {ValidationHelper.GetSummary(validationResult)}");
                }

                ScenarioCommand.ReportValidationSummary(invalidCount, result.Graph.AllResources.Count);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"X Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}
