using Ignixa.Abstractions;
using System.CommandLine;
using System.Text.Json;
using Ignixa.FhirFakes.Cli.Discovery;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Cli.Commands;

/// <summary>
/// Command for generating predefined FHIR scenarios.
/// </summary>
internal static class ScenarioCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var scenarioCommand = new Command("scenario", "Generate a predefined FHIR scenario");

        var scenarioNameArg = new Argument<string>("scenarioName")
        {
            Description = "The scenario name (e.g., DiabeticPatient)"
        };

        var outOption = new Option<string>("--out")
        {
            Description = "Output folder for generated files",
            Required = true
        };

        var resolvedReferencesOption = new Option<bool>("--resolved-references")
        {
            Description = "Create a batch bundle instead of references"
        };

        var validateOption = new Option<bool>("--validate")
        {
            Description = "Validate generated resources against schema", DefaultValueFactory = _ => false
        };
        

        scenarioCommand.Arguments.Add(scenarioNameArg);
        scenarioCommand.Options.Add(outOption);
        scenarioCommand.Options.Add(resolvedReferencesOption);
        scenarioCommand.Options.Add(validateOption);

        scenarioCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var scenarioName = parseResult.GetValue(scenarioNameArg)!;
            var outFolder = parseResult.GetValue(outOption)!;
            var resolvedReferences = parseResult.GetValue(resolvedReferencesOption);
            var validate = parseResult.GetValue(validateOption);

            await HandleScenarioCommand(schemaProvider, fhirVersion, scenarioName, outFolder, resolvedReferences, validate);
        });

        return scenarioCommand;
    }

    private static async Task HandleScenarioCommand(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string scenarioName,
        string outFolder,
        bool resolvedReferences,
        bool validate)
    {
        try
        {
            // Ensure output directory exists
            Directory.CreateDirectory(outFolder);

            // Discover and create the scenario
            var context = ScenarioDiscovery.CreateScenario(schemaProvider, scenarioName);
            if (context == null)
            {
                Console.WriteLine($"X Unknown scenario: {scenarioName}");
                Console.WriteLine("Available scenarios:");
                foreach (var name in ScenarioDiscovery.GetScenarioNames())
                {
                    Console.WriteLine($"  - {name}");
                }
                return;
            }

            var id = Guid.NewGuid().ToString();
            var filename = $"{fhirVersion}-bundle-{scenarioName}-{id}.json";
            var outputPath = Path.Combine(outFolder, filename);

            JsonSerializerOptions options = new()
            {
                WriteIndented = true
            };

            // Rewrite references if using batch bundle (resolved references)
            // Transaction bundles use urn:uuid by default, batch bundles need Patient/id format
            if (resolvedReferences)
            {
                context.RewriteReferences(schemaProvider.ReferenceMetadataProvider, ReferenceFormat.Resolved);
            }

            // Create a transaction bundle (default behavior)
            // Use ToBatchBundle if resolved references is requested
            var bundle = resolvedReferences ? context.ToBatchBundle() : context.ToBundle();
            var json = JsonSerializer.Serialize(bundle.MutableNode, options);
            await File.WriteAllTextAsync(outputPath, json);

            var bundleType = resolvedReferences ? "batch" : "transaction";
            Console.WriteLine($"Generated scenario bundle ({bundleType}): {outputPath}");
            Console.WriteLine($"  Resources: {context.AllResources.Count}");

            // Validate each resource in the scenario if requested
            if (validate)
            {
                Console.WriteLine("\n-------------------------------------------------------------------");
                Console.WriteLine("Validating generated resources...");
                Console.WriteLine("-------------------------------------------------------------------");

                var validationResults = new Dictionary<string, Ignixa.Validation.ValidationResult>();
                foreach (var resource in context.AllResources)
                {
                    var resourceType = resource.MutableNode["resourceType"]?.ToString() ?? "Unknown";
                    var resourceId = resource.MutableNode["id"]?.ToString() ?? "unknown";
                    var key = $"{resourceType}/{resourceId}";

                    var result = ValidationHelper.ValidateResource(resource.MutableNode, schemaProvider);
                    validationResults[key] = result;

                    var summary = ValidationHelper.GetSummary(result);
                    Console.WriteLine($"  {key}: {summary}");
                }

                // Show summary of validation results
                var invalidCount = validationResults.Count(r => !r.Value.IsValid);
                if (invalidCount > 0)
                {
                    Console.WriteLine($"\n  {invalidCount} resource(s) have validation issues");
                }
                else
                {
                    Console.WriteLine($"\n  All {context.AllResources.Count} resource(s) passed validation");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"X Error: {ex.Message}");
        }
    }
}
