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
        var includeInvalidOption = new Option<bool>("--include-invalid") { Description = "Include non-validity-preserving (MayViolate/AlwaysInvalid) strategies when edge-cases are enabled", DefaultValueFactory = _ => false };
        var densityOption = new Option<string?>("--density") { Description = "Generation density: minimal|realistic|maximal (default minimal). realistic/maximal use the schema generator for ANY resource type and therefore IGNORE --firstname/--surname/--from and the Observation stateName specialization. realistic currently behaves identically to minimal." };
        var verboseOption = new Option<bool>("--verbose") { Description = "Print full exception details (type and stack trace) on error", DefaultValueFactory = _ => false };

        resourceCommand.Arguments.Add(resourceTypeArg);
        resourceCommand.Arguments.Add(stateNameArg);
        resourceCommand.Options.Add(outOption);
        resourceCommand.Options.Add(firstnameOption);
        resourceCommand.Options.Add(surnameOption);
        resourceCommand.Options.Add(fromOption);
        resourceCommand.Options.Add(validateOption);
        resourceCommand.Options.Add(edgeCasesOption);
        resourceCommand.Options.Add(seedOption);
        resourceCommand.Options.Add(includeInvalidOption);
        resourceCommand.Options.Add(densityOption);
        resourceCommand.Options.Add(verboseOption);

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
            var includeInvalid = parseResult.GetValue(includeInvalidOption);
            var verbose = parseResult.GetValue(verboseOption);
            var seed = explicitSeed ?? (edgeCasesEnabled ? GenerateSeed() : 0);

            if (!TryParseDensity(parseResult.GetValue(densityOption), out var density))
            {
                await Console.Error.WriteLineAsync($"✗ Invalid --density value '{parseResult.GetValue(densityOption)}'. Use minimal, realistic, or maximal.");
                Environment.ExitCode = 2;
                return;
            }

            if (edgeCasesEnabled && explicitSeed is null)
                Console.WriteLine($"Seed: {seed}  (pass --seed {seed} to replay)");

            await HandleResourceCommand(schemaProvider, fhirVersion, resourceType, stateName, outFolder,
                firstname, surname, from, validate, edgeCasesEnabled, selectors, seed, explicitSeed, includeInvalid, density, verbose, cancellationToken);
        });

        return resourceCommand;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
    private static int GenerateSeed() => Random.Shared.Next();

    private static bool TryParseDensity(string? value, out GenerationDensity density)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            density = GenerationDensity.Minimal;
            return true;
        }

        var trimmed = value.Trim();
        if (char.IsDigit(trimmed[0]))
        {
            density = GenerationDensity.Minimal;
            return false;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out density)
            && Enum.IsDefined(density);
    }

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
        int? explicitSeed,
        bool includeInvalid,
        GenerationDensity density,
        bool verbose,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(outFolder);

            JsonSerializerOptions options = new()
            {
                WriteIndented = true
            };

            if (density != GenerationDensity.Minimal)
            {
                await HandleGenericDensity(schemaProvider, fhirVersion, resourceType, outFolder, validate,
                    edgeCasesEnabled, selectors, seed, explicitSeed, includeInvalid, density, options, cancellationToken);
            }
            else if (resourceType.Equals("Patient", StringComparison.OrdinalIgnoreCase))
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
                var manifest = ApplyEdgeCases(schemaProvider, patient, edgeCasesEnabled, selectors, seed, includeInvalid);

                var id = patient.MutableNode["id"]?.ToString() ?? Guid.NewGuid().ToString();
                var filename = $"{fhirVersion}-patient-{id}.json";
                var outputPath = Path.Combine(outFolder, filename);

                var json = JsonSerializer.Serialize(patient.MutableNode, options);
                await File.WriteAllTextAsync(outputPath, json, cancellationToken);

                Console.WriteLine($"✓ Generated Patient: {outputPath}");

                await ReportManifestAsync(outputPath, manifest, cancellationToken);

                if (validate)
                    RunValidation(patient.MutableNode, schemaProvider, "Patient", fhirVersion, includeInvalid);
            }
            else if (resourceType.Equals("Observation", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(stateName))
            {
                var observationState = StateDiscovery.CreateObservationState(stateName);
                if (observationState == null)
                {
                    await Console.Error.WriteLineAsync($"✗ Unknown observation state: {stateName}");
                    await Console.Error.WriteLineAsync("Available states:");
                    foreach (var name in StateDiscovery.GetObservationStateNames())
                        await Console.Error.WriteLineAsync($"  - {name}");
                    Environment.ExitCode = 2;
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
                    var manifest = ApplyEdgeCases(schemaProvider, observation, edgeCasesEnabled, selectors, seed, includeInvalid);

                    var id = observation.MutableNode["id"]?.ToString() ?? Guid.NewGuid().ToString();
                    var filename = $"{fhirVersion}-observation-{stateName}-{id}.json";
                    var outputPath = Path.Combine(outFolder, filename);

                    var json = JsonSerializer.Serialize(observation.MutableNode, options);
                    await File.WriteAllTextAsync(outputPath, json, cancellationToken);

                    Console.WriteLine($"✓ Generated Observation ({stateName}): {outputPath}");

                    await ReportManifestAsync(outputPath, manifest, cancellationToken);

                    if (validate)
                        RunValidation(observation.MutableNode, schemaProvider, "Observation", fhirVersion, includeInvalid);
                }
            }
            else
            {
                await Console.Error.WriteLineAsync($"✗ Resource type '{resourceType}' is not supported or requires a state name.");
                await Console.Error.WriteLineAsync("Supported: Patient, Observation <stateName>");
                Environment.ExitCode = 2;
                return;
            }
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("✗ Operation cancelled.");
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(verbose
                ? $"✗ Error ({ex.GetType().Name}): {ex.Message}{Environment.NewLine}{ex.StackTrace}"
                : $"✗ Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Lowercase used for filename normalization, matching existing CLI conventions")]
    private static async Task HandleGenericDensity(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string resourceType,
        string outFolder,
        bool validate,
        bool edgeCasesEnabled,
        string[] selectors,
        int seed,
        int? explicitSeed,
        bool includeInvalid,
        GenerationDensity density,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        var faker = explicitSeed is { } s
            ? new SchemaBasedFhirResourceFaker(schemaProvider, s) { Density = density }
            : new SchemaBasedFhirResourceFaker(schemaProvider) { Density = density };

        var resource = faker.Generate(resourceType);
        var manifest = ApplyEdgeCases(schemaProvider, resource, edgeCasesEnabled, selectors, seed, includeInvalid);

        var id = resource.MutableNode["id"]?.ToString() ?? Guid.NewGuid().ToString();
        var filename = $"{fhirVersion}-{resourceType.ToLowerInvariant()}-{density.ToString().ToLowerInvariant()}-{id}.json";
        var outputPath = Path.Combine(outFolder, filename);

        var json = JsonSerializer.Serialize(resource.MutableNode, options);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);

        Console.WriteLine($"✓ Generated {resourceType} ({density}): {outputPath}");

        await ReportManifestAsync(outputPath, manifest, cancellationToken);

        if (validate)
            RunValidation(resource.MutableNode, schemaProvider, resourceType, fhirVersion, includeInvalid);
    }

    private static MutationManifest? ApplyEdgeCases(IFhirSchemaProvider schemaProvider, ResourceJsonNode resource, bool enabled, string[] selectors, int seed, bool includeInvalid)
    {
        if (!enabled)
            return null;

        var catalog = EdgeCaseCatalog.CreateDefault();
        var strategies = catalog.Resolve(selectors, out var unmatched);

        if (unmatched.Count > 0)
        {
            foreach (var sel in unmatched)
                Console.Error.WriteLine($"⚠  Unknown --edge-cases selector: '{sel}'");
            Environment.ExitCode = 2;
        }

        var pipeline = new EdgeCasePipeline(seed, schemaProvider);
        return pipeline.Apply(resource, strategies, includeInvalid);
    }

    private static async Task ReportManifestAsync(string resourcePath, MutationManifest? manifest, CancellationToken cancellationToken)
    {
        if (manifest is null)
            return;

        PrintEdgeCaseSummary(manifest);
        await WriteManifestSafeAsync(resourcePath, manifest, cancellationToken);
    }

    private static async Task WriteManifestSafeAsync(string resourcePath, MutationManifest manifest, CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.ChangeExtension(resourcePath, null) + ".manifest.json";
        try
        {
            await File.WriteAllTextAsync(manifestPath, manifest.ToJson(), cancellationToken);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"✗ Resource written but manifest failed ({manifestPath}): {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void PrintEdgeCaseSummary(MutationManifest manifest)
    {
        Console.WriteLine($"  Edge cases: seed={manifest.Seed}, mutations={manifest.Mutations.Count}");
        foreach (var group in manifest.Mutations.GroupBy(m => m.Category))
            Console.WriteLine($"    {group.Key}: {group.Count()}");
    }

    /// <summary>
    /// Runs FHIR schema validation and prints results. Sets <c>Environment.ExitCode = 1</c>
    /// when validation fails, unless <paramref name="includeInvalid"/> is true
    /// (invalidity was deliberately requested via --include-invalid).
    /// </summary>
    private static void RunValidation(System.Text.Json.Nodes.JsonNode node, IFhirSchemaProvider schemaProvider, string resourceType, string fhirVersion, bool includeInvalid)
    {
        var validationResult = ValidationHelper.ValidateResource(node, schemaProvider);
        if (!validationResult.IsValid)
        {
            Console.Error.WriteLine($"\n⚠  Validation Issues Detected:");
            ValidationHelper.DisplayResults(validationResult, resourceType, fhirVersion, verbose: false);
            if (!includeInvalid)
                Environment.ExitCode = 1;
        }
        else
        {
            Console.WriteLine("✓ Validation passed");
        }
    }
}
