using System.CommandLine;
using System.Text.Json;
using Ignixa.FhirFaker.Cli.Discovery;
using Ignixa.Specification;

namespace Ignixa.FhirFaker.Cli.Commands;

/// <summary>
/// Command for generating predefined FHIR scenarios.
/// </summary>
internal static class ScenarioCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider)
    {
        var scenarioCommand = new Command("scenario", "Generate a predefined FHIR scenario");

        var scenarioNameArg = new Argument<string>("scenarioName", "The scenario name (e.g., DiabeticPatient)");
        var resolvedReferencesOption = new Option<bool>("--resolved-references", "Create a batch bundle instead of references");

        scenarioCommand.AddArgument(scenarioNameArg);
        scenarioCommand.AddOption(resolvedReferencesOption);

        scenarioCommand.SetHandler(async (scenarioName, resolvedReferences) =>
        {
            await HandleScenarioCommand(schemaProvider, scenarioName, resolvedReferences);
        }, scenarioNameArg, resolvedReferencesOption);

        return scenarioCommand;
    }

    private static async Task HandleScenarioCommand(
        IFhirSchemaProvider schemaProvider,
        string scenarioName,
        bool resolvedReferences)
    {
        try
        {
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
            var filename = $"bundle-{scenarioName}-{id}.json";

            JsonSerializerOptions options = new()
            {
                WriteIndented = true
            };

            // Create a transaction bundle (default behavior)
            // Use ToBatchBundle if resolved references is requested
            var bundle = resolvedReferences ? context.ToBatchBundle() : context.ToBundle();
            var json = JsonSerializer.Serialize(bundle.MutableNode, options);
            await File.WriteAllTextAsync(filename, json);

            var bundleType = resolvedReferences ? "batch" : "transaction";
            Console.WriteLine($"✓ Generated scenario bundle ({bundleType}): {filename}");
            Console.WriteLine($"  Resources: {context.AllResources.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
        }
    }
}
