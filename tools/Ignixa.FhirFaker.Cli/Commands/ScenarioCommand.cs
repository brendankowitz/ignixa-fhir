using System.CommandLine;
using System.Text.Json;
using Ignixa.FhirFaker.Cli.Discovery;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Specification;

namespace Ignixa.FhirFaker.Cli.Commands;

/// <summary>
/// Command for generating predefined FHIR scenarios.
/// </summary>
internal static class ScenarioCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var scenarioCommand = new Command("scenario", "Generate a predefined FHIR scenario");

        var scenarioNameArg = new Argument<string>("scenarioName", "The scenario name (e.g., DiabeticPatient)");
        var outOption = new Option<string>("--out", "Output folder for generated files") { IsRequired = true };
        var resolvedReferencesOption = new Option<bool>("--resolved-references", "Create a batch bundle instead of references");

        scenarioCommand.AddArgument(scenarioNameArg);
        scenarioCommand.AddOption(outOption);
        scenarioCommand.AddOption(resolvedReferencesOption);

        scenarioCommand.SetHandler(async (scenarioName, outFolder, resolvedReferences) =>
        {
            await HandleScenarioCommand(schemaProvider, fhirVersion, scenarioName, outFolder, resolvedReferences);
        }, scenarioNameArg, outOption, resolvedReferencesOption);

        return scenarioCommand;
    }

    private static async Task HandleScenarioCommand(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string scenarioName,
        string outFolder,
        bool resolvedReferences)
    {
        try
        {
            // Ensure output directory exists
            Directory.CreateDirectory(outFolder);

            // Discover and create the scenario
            var context = ScenarioDiscovery.CreateScenario(schemaProvider, scenarioName);
            if (context == null)
            {
                Console.WriteLine($"✗ Unknown scenario: {scenarioName}");
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
            Console.WriteLine($"✓ Generated scenario bundle ({bundleType}): {outputPath}");
            Console.WriteLine($"  Resources: {context.AllResources.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
        }
    }
}
