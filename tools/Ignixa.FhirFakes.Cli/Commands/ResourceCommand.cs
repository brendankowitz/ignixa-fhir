using Ignixa.Abstractions;
using System.CommandLine;
using System.Text.Json;
using Ignixa.FhirFakes.Cli.Discovery;
using Ignixa.FhirFakes;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Cli.Commands;

/// <summary>
/// Command for generating single FHIR resources.
/// </summary>
internal static class ResourceCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var resourceCommand = new Command("resource", "Generate a single FHIR resource");

        var resourceTypeArg = new Argument<string>("resourceType") { Description = "The FHIR resource type (e.g., Patient, Observation)" };
        var stateNameArg = new Argument<string?>("stateName") { Description = "Optional state/builder name (e.g., BloodGlucose for Observation)", Arity = ArgumentArity.ZeroOrOne, DefaultValueFactory = _ => null };

        var outOption = new Option<string>("--out") { Description = "Output folder for generated files", Required = true };
        var firstnameOption = new Option<string?>("--firstname") { Description = "Patient first name" };
        var surnameOption = new Option<string?>("--surname") { Description = "Patient surname" };
        var fromOption = new Option<string?>("--from") { Description = "City to generate from" };
        var validateOption = new Option<bool>("--validate") { Description = "Validate generated resource against schema", DefaultValueFactory = _ => false };
        var edgeCasesOption = new Option<string?>("--edge-cases") { Description = "Enable edge-case perturbation. Optionally specify comma-separated selectors (families or categories).", Arity = ArgumentArity.ZeroOrOne };
        var seedOption = new Option<int?>("--seed") { Description = "Seed for reproducible edge-case generation" };

        resourceCommand.Arguments.Add(resourceTypeArg);
        resourceCommand.Arguments.Add(stateNameArg);
        resourceCommand.Options.Add(outOption);
        resourceCommand.Options.Add(firstnameOption);
        resourceCommand.Options.Add(surnameOption);
        resourceCommand.Options.Add(fromOption);
        resourceCommand.Options.Add(validateOption);
        resourceCommand.Options.Add(edgeCasesOption);
        resourceCommand.Options.Add(seedOption);

        resourceCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var resourceType = parseResult.GetValue(resourceTypeArg)!;
            var stateName = parseResult.GetValue(stateNameArg);
            var outFolder = parseResult.GetValue(outOption)!;
            var firstname = parseResult.GetValue(firstnameOption);
            var surname = parseResult.GetValue(surnameOption);
            var from = parseResult.GetValue(fromOption);
            var validate = parseResult.GetValue(validateOption);

            var edgeCasesEnabled = parseResult.GetResult(edgeCasesOption) is not null;
            var edgeCasesValue = parseResult.GetValue(edgeCasesOption);
            var selectors = ParseSelectors(edgeCasesValue);
            var explicitSeed = parseResult.GetValue(seedOption);
            var seed = explicitSeed ?? (edgeCasesEnabled ? GenerateSeed() : 0);

            if (edgeCasesEnabled && explicitSeed is null)
                Console.WriteLine($"Seed: {seed}  (pass --seed {seed} to replay)");

            await HandleResourceCommand(schemaProvider, fhirVersion, resourceType, stateName, outFolder,
                firstname, surname, from, validate, edgeCasesEnabled, selectors, seed, explicitSeed);
        });

        return resourceCommand;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
    private static int GenerateSeed() => Random.Shared.Next();

    private static string[] ParseSelectors(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static async Task HandleResourceCommand(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string resourceType,
        string? stateName,
        string outFolder,
        string? firstname,
        string? surname,
        string? from,
        bool validate,
        bool edgeCasesEnabled,
        string[] selectors,
        int seed,
        int? explicitSeed)
    {
        try
        {
            Directory.CreateDirectory(outFolder);

            JsonSerializerOptions options = new()
            {
                WriteIndented = true
            };

            if (resourceType.Equals("Patient", StringComparison.OrdinalIgnoreCase))
            {
                var builder = PatientBuilderFactory.Create(schemaProvider, explicitSeed);

                if (!string.IsNullOrEmpty(firstname))
                    builder.WithGivenName(firstname);

                if (!string.IsNullOrEmpty(surname))
                    builder.WithFamilyName(surname);

                if (!string.IsNullOrEmpty(from))
                {
                    var city = StateDiscovery.FindCity(from);
                    if (city != null)
                        builder.FromCity(city);
                    else
                        builder.WithCity(from);
                }

                var patient = builder.Build();
                var manifest = ApplyEdgeCases(patient, edgeCasesEnabled, selectors, seed);

                var id = patient.MutableNode["id"]?.ToString() ?? Guid.NewGuid().ToString();
                var filename = $"{fhirVersion}-patient-{id}.json";
                var outputPath = Path.Combine(outFolder, filename);

                var json = JsonSerializer.Serialize(patient.MutableNode, options);
                await File.WriteAllTextAsync(outputPath, json);

                Console.WriteLine($"✓ Generated Patient: {outputPath}");

                if (manifest is not null)
                {
                    PrintEdgeCaseSummary(manifest);
                    await WriteManifestAsync(outputPath, manifest);
                }

                if (validate)
                    RunValidation(patient.MutableNode, schemaProvider, "Patient", fhirVersion);
            }
            else if (resourceType.Equals("Observation", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(stateName))
            {
                var observationState = StateDiscovery.CreateObservationState(stateName);
                if (observationState == null)
                {
                    Console.WriteLine($"✗ Unknown observation state: {stateName}");
                    Console.WriteLine("Available states:");
                    foreach (var name in StateDiscovery.GetObservationStateNames())
                        Console.WriteLine($"  - {name}");
                    return;
                }

                var faker = new SchemaBasedFhirResourceFaker(schemaProvider);
                var context = new Ignixa.FhirFakes.Scenarios.ScenarioContext();
                var patient = PatientBuilderFactory.Create(schemaProvider).Build();
                context.Patient = patient;

                observationState.Execute(context, faker);

                var allResources = context.AllResources;
                if (allResources.Count > 0)
                {
                    var observation = allResources[allResources.Count - 1];
                    var manifest = ApplyEdgeCases(observation, edgeCasesEnabled, selectors, seed);

                    var id = observation.MutableNode["id"]?.ToString() ?? Guid.NewGuid().ToString();
                    var filename = $"{fhirVersion}-observation-{stateName}-{id}.json";
                    var outputPath = Path.Combine(outFolder, filename);

                    var json = JsonSerializer.Serialize(observation.MutableNode, options);
                    await File.WriteAllTextAsync(outputPath, json);

                    Console.WriteLine($"✓ Generated Observation ({stateName}): {outputPath}");

                    if (manifest is not null)
                    {
                        PrintEdgeCaseSummary(manifest);
                        await WriteManifestAsync(outputPath, manifest);
                    }

                    if (validate)
                        RunValidation(observation.MutableNode, schemaProvider, "Observation", fhirVersion);
                }
            }
            else
            {
                Console.WriteLine($"✗ Resource type '{resourceType}' is not supported or requires a state name.");
                Console.WriteLine("Supported: Patient, Observation <stateName>");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
        }
    }

    private static MutationManifest? ApplyEdgeCases(ResourceJsonNode resource, bool enabled, string[] selectors, int seed)
    {
        if (!enabled)
            return null;

        var catalog = EdgeCaseCatalog.CreateDefault();
        var strategies = catalog.Resolve(selectors);
        var pipeline = new EdgeCasePipeline(seed);
        return pipeline.Apply(resource, strategies);
    }

    private static async Task WriteManifestAsync(string resourcePath, MutationManifest manifest)
    {
        var manifestPath = Path.ChangeExtension(resourcePath, null) + ".manifest.json";
        await File.WriteAllTextAsync(manifestPath, manifest.ToJson());
    }

    private static void PrintEdgeCaseSummary(MutationManifest manifest)
    {
        Console.WriteLine($"  Edge cases: seed={manifest.Seed}, mutations={manifest.Mutations.Count}");
        foreach (var group in manifest.Mutations.GroupBy(m => m.Category))
            Console.WriteLine($"    {group.Key}: {group.Count()}");
    }

    private static void RunValidation(System.Text.Json.Nodes.JsonNode node, IFhirSchemaProvider schemaProvider, string resourceType, string fhirVersion)
    {
        var validationResult = ValidationHelper.ValidateResource(node, schemaProvider);
        if (!validationResult.IsValid)
        {
            Console.WriteLine($"\n⚠️  Validation Issues Detected:");
            ValidationHelper.DisplayResults(validationResult, resourceType, fhirVersion, verbose: false);
        }
        else
        {
            Console.WriteLine($"✓ Validation passed");
        }
    }
}
