using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Cli.Commands;

/// <summary>
/// Command for generating FHIR patient populations.
/// </summary>
internal static class PopulationCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var populationCommand = new Command("population", "Generate a population of patients");

        var outOption = new Option<string>("--out") { Description = "Output folder for generated files", Required = true };
        var fromOption = new Option<string?>("--from") { Description = "City or state to generate from" };
        var countOption = new Option<int>("--count") { Description = "Number of patients to generate", DefaultValueFactory = _ => 10 };
        var resolvedReferencesOption = new Option<bool>("--resolved-references") { Description = "Create batch bundles instead of references" };
        var ndjsonOption = new Option<bool>("--ndjson") { Description = "Write ndjson files instead of bundles (implies --resolved-references)" };

        populationCommand.Options.Add(outOption);
        populationCommand.Options.Add(fromOption);
        populationCommand.Options.Add(countOption);
        populationCommand.Options.Add(resolvedReferencesOption);
        populationCommand.Options.Add(ndjsonOption);

        populationCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var outFolder = parseResult.GetValue(outOption)!;
            var from = parseResult.GetValue(fromOption);
            var count = parseResult.GetValue(countOption);
            var resolvedReferences = parseResult.GetValue(resolvedReferencesOption);
            var ndjson = parseResult.GetValue(ndjsonOption);
            await HandlePopulationCommand(schemaProvider, fhirVersion, outFolder, from, count, resolvedReferences, ndjson);
        });

        return populationCommand;
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
    private static async Task HandlePopulationCommand(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string outFolder,
        string? from,
        int count,
        bool resolvedReferences,
        bool ndjson)
    {
        try
        {
            // Ensure output directory exists
            Directory.CreateDirectory(outFolder);

            // ndjson implies resolved references
            if (ndjson)
            {
                resolvedReferences = true;
            }

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

            if (ndjson)
            {
                // Generate ndjson files - one file per resource type across all patients
                await WriteNdjsonFiles(schemaProvider, fhirVersion, outFolder, state, count, contexts);
            }
            else if (resolvedReferences)
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

                // Rewrite all contexts' references to urn:uuid format for transaction bundle
                foreach (var context in contexts)
                {
                    context.RewriteReferences(schemaProvider.ReferenceMetadataProvider, ReferenceFormat.UrnUuid);
                }

                // Combine all contexts into a single transaction bundle
                var entries = new System.Text.Json.Nodes.JsonArray();
                int totalResources = 0;

                foreach (var context in contexts)
                {
                    var bundle = context.ToBundle();
                    if (bundle.MutableNode["entry"] is System.Text.Json.Nodes.JsonArray bundleEntries)
                    {
                        foreach (var entry in bundleEntries)
                        {
                            if (entry is not null)
                            {
                                entries.Add(entry.DeepClone());
                                totalResources++;
                            }
                        }
                    }
                }

                // Create combined bundle
                var combinedBundle = new System.Text.Json.Nodes.JsonObject
                {
                    ["resourceType"] = "Bundle",
                    ["id"] = id,
                    ["type"] = "transaction",
                    ["entry"] = entries
                };

                var json = JsonSerializer.Serialize(combinedBundle, options);
                await File.WriteAllTextAsync(outputPath, json);

                Console.WriteLine($"✓ Generated population bundle: {outputPath}");
                Console.WriteLine($"  Total resources: {totalResources}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    private static async Task WriteNdjsonFiles(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string outFolder,
        string state,
        int count,
        List<ScenarioContext> contexts)
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = false // ndjson should not be indented
        };

        // Rewrite references for all contexts to resolved format
        foreach (var context in contexts)
        {
            context.RewriteReferences(schemaProvider.ReferenceMetadataProvider, ReferenceFormat.Resolved);
        }

        // Group all resources by type across all patients
        var allResourcesByType = contexts
            .SelectMany(c => c.AllResources)
            .GroupBy(r => r.ResourceType)
            .ToDictionary(g => g.Key, g => g.ToList());

        int totalResources = 0;

        // Write one ndjson file per resource type
        foreach (var (resourceType, resources) in allResourcesByType)
        {
            var id = Guid.NewGuid().ToString();
            var sanitizedState = SanitizeFileName(state);
            var filename = $"{fhirVersion}-population-{sanitizedState}-{resourceType}-{count}-{id}.ndjson";
            var outputPath = Path.Combine(outFolder, filename);

            // Write resources as newline-delimited JSON
            await using var writer = new StreamWriter(outputPath);
            foreach (var resource in resources)
            {
                var json = JsonSerializer.Serialize(resource.MutableNode, options);
                await writer.WriteLineAsync(json);
                totalResources++;
            }

            Console.WriteLine($"  Written {resources.Count} {resourceType} resources to {Path.GetFileName(outputPath)}");
        }

        Console.WriteLine($"✓ Generated {count} patients in ndjson format from {state}");
        Console.WriteLine($"  Total resources: {totalResources}");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }
}
