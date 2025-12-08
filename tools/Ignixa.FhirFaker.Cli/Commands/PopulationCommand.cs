using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Specification;

namespace Ignixa.FhirFaker.Cli.Commands;

/// <summary>
/// Command for generating FHIR patient populations.
/// </summary>
internal static class PopulationCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var populationCommand = new Command("population", "Generate a population of patients");

        var outOption = new Option<string>("--out", "Output folder for generated files") { IsRequired = true };
        var fromOption = new Option<string?>("--from", "City or state to generate from");
        var countOption = new Option<int>("--count", () => 10, "Number of patients to generate");
        var resolvedReferencesOption = new Option<bool>("--resolved-references", "Create batch bundles instead of references");

        populationCommand.AddOption(outOption);
        populationCommand.AddOption(fromOption);
        populationCommand.AddOption(countOption);
        populationCommand.AddOption(resolvedReferencesOption);

        populationCommand.SetHandler(async (outFolder, from, count, resolvedReferences) =>
        {
            await HandlePopulationCommand(schemaProvider, fhirVersion, outFolder, from, count, resolvedReferences);
        }, outOption, fromOption, countOption, resolvedReferencesOption);

        return populationCommand;
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
    private static async Task HandlePopulationCommand(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string outFolder,
        string? from,
        int count,
        bool resolvedReferences)
    {
        try
        {
            // Ensure output directory exists
            Directory.CreateDirectory(outFolder);

            var generator = new PopulationGenerator(schemaProvider);
            
            // Determine the state to generate from
            string state;
            if (string.IsNullOrEmpty(from))
            {
                // Default to a random available state
                var random = new Random();
                state = generator.AvailableStates[random.Next(generator.AvailableStates.Count)];
                Console.WriteLine($"No location specified, using: {state}");
            }
            else
            {
                // Check if it's a city first, then fall back to state
                var city = generator.AvailableCities.FirstOrDefault(c => 
                    c.Name.Equals(from, StringComparison.OrdinalIgnoreCase));
                
                if (city != null)
                {
                    state = city.State;
                    Console.WriteLine($"Generating population from {city.Name}, {state}");
                }
                else
                {
                    // Try as a state name
                    state = generator.AvailableStates.FirstOrDefault(s => 
                        s.Equals(from, StringComparison.OrdinalIgnoreCase)) ?? from;
                    Console.WriteLine($"Generating population from {state}");
                }
            }

            Console.WriteLine($"Generating {count} patients...");

            JsonSerializerOptions options = new()
            {
                WriteIndented = true
            };

            var contexts = generator.Generate(state, count).ToList();

            if (resolvedReferences)
            {
                // Generate separate bundle files for each patient
                for (int i = 0; i < contexts.Count; i++)
                {
                    var context = contexts[i];
                    
                    // Rewrite references for batch bundle (resolved format)
                    context.RewriteReferences(schemaProvider.ReferenceMetadataProvider, ReferenceFormat.Resolved);
                    
                    var id = Guid.NewGuid().ToString();
                    var sanitizedState = SanitizeFileName(state);
                    var filename = $"{fhirVersion}-bundle-population-{sanitizedState}-{count}-{i + 1}-{id}.json";
                    var outputPath = Path.Combine(outFolder, filename);

                    var bundle = context.ToBatchBundle();
                    var json = JsonSerializer.Serialize(bundle.MutableNode, options);
                    await File.WriteAllTextAsync(outputPath, json);

                    if ((i + 1) % 10 == 0 || i == contexts.Count - 1)
                    {
                        Console.WriteLine($"  Progress: {i + 1}/{count} patients generated");
                    }
                }

                Console.WriteLine($"✓ Generated {count} patient bundles from {state}");
            }
            else
            {
                // Generate a single large transaction bundle with all patients
                // Check if we have any contexts
                if (contexts.Count == 0)
                {
                    Console.WriteLine("✗ No patients were generated");
                    return;
                }

                var id = Guid.NewGuid().ToString();
                var sanitizedState = SanitizeFileName(state);
                var filename = $"{fhirVersion}-bundle-population-{sanitizedState}-{count}-{id}.json";
                var outputPath = Path.Combine(outFolder, filename);

                // Combine all contexts into a single bundle
                var allResources = contexts.SelectMany(c => c.AllResources).ToList();
                
                // Create a transaction bundle with all resources
                var firstContext = contexts[0];
                var combinedBundle = firstContext.ToBundle();
                
                // Replace entries with all resources
                var entries = new System.Text.Json.Nodes.JsonArray();
                foreach (var resource in allResources)
                {
                    var entry = new System.Text.Json.Nodes.JsonObject
                    {
                        ["resource"] = resource.MutableNode,
                        ["request"] = new System.Text.Json.Nodes.JsonObject
                        {
                            ["method"] = "POST",
                            ["url"] = resource.ResourceType
                        }
                    };
                    entries.Add(entry);
                }
                
                combinedBundle.MutableNode["entry"] = entries;
                
                var json = JsonSerializer.Serialize(combinedBundle.MutableNode, options);
                await File.WriteAllTextAsync(outputPath, json);

                Console.WriteLine($"✓ Generated population bundle: {outputPath}");
                Console.WriteLine($"  Total resources: {allResources.Count}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }
}
