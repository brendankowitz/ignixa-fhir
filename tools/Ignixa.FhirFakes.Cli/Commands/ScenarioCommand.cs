using Ignixa.Abstractions;
using System.CommandLine;
using System.Globalization;
using System.Text.Json;
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

        var paramOption = new Option<string[]>("--param")
        {
            Description = "Override a scenario parameter, format name=value (repeatable, e.g. --param age=60 --param severity=3)",
            DefaultValueFactory = _ => []
        };

        scenarioCommand.Arguments.Add(scenarioNameArg);
        scenarioCommand.Options.Add(outOption);
        scenarioCommand.Options.Add(resolvedReferencesOption);
        scenarioCommand.Options.Add(validateOption);
        scenarioCommand.Options.Add(paramOption);

        scenarioCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var scenarioName = parseResult.GetValue(scenarioNameArg)!;
            var outFolder = parseResult.GetValue(outOption)!;
            var resolvedReferences = parseResult.GetValue(resolvedReferencesOption);
            var validate = parseResult.GetValue(validateOption);
            var paramValues = parseResult.GetValue(paramOption) ?? [];

            await HandleScenarioCommand(schemaProvider, fhirVersion, scenarioName, outFolder, resolvedReferences, validate, paramValues);
        });

        return scenarioCommand;
    }

    private static async Task HandleScenarioCommand(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string scenarioName,
        string outFolder,
        bool resolvedReferences,
        bool validate,
        string[] paramValues)
    {
        try
        {
            // Ensure output directory exists
            Directory.CreateDirectory(outFolder);

            // Discover the scenario
            var scenario = ScenarioCatalog.Find(scenarioName);
            if (scenario == null)
            {
                Console.WriteLine($"X Unknown scenario: {scenarioName}");
                Console.WriteLine("Available scenarios:");
                foreach (var name in ScenarioCatalog.All().Select(s => s.Id).OrderBy(s => s))
                {
                    Console.WriteLine($"  - {name}");
                }
                Environment.ExitCode = 2;
                return;
            }

            if (!TryParseParameterOverrides(scenario, paramValues, out var overrides, out var parseError))
            {
                Console.WriteLine($"X {parseError}");
                Environment.ExitCode = 2;
                return;
            }

            ScenarioContext context;
            try
            {
                context = ScenarioCatalog.Invoke(scenario, schemaProvider, overrides);
            }
            catch (ScenarioInvocationException ex)
            {
                Console.WriteLine($"X Error: {ex.Message}");
                Environment.ExitCode = 1;
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

    /// <summary>
    /// Parses <c>--param name=value</c> overrides into a name-to-value dictionary, converting each raw
    /// string to the scenario parameter's declared CLR type. Internal (not private) so
    /// <c>ScenarioCommandParameterOverrideTests</c> can call it directly.
    /// </summary>
    internal static bool TryParseParameterOverrides(
        DiscoveredScenario scenario,
        string[] paramValues,
        out Dictionary<string, object?> overrides,
        out string? error)
    {
        overrides = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        error = null;

        foreach (var raw in paramValues)
        {
            var separatorIndex = raw.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                error = $"Invalid --param value '{raw}'. Expected format name=value.";
                return false;
            }

            var name = raw[..separatorIndex];
            var rawValue = raw[(separatorIndex + 1)..];

            var parameter = scenario.Parameters.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (parameter == null)
            {
                error = $"Scenario '{scenario.Id}' has no parameter named '{name}'. Available: {string.Join(", ", scenario.Parameters.Select(p => p.Name))}";
                return false;
            }

            if (!TryConvert(rawValue, parameter.Type, out var converted))
            {
                error = $"Cannot convert value '{rawValue}' for parameter '{name}' to {parameter.Type.Name}.";
                return false;
            }

            overrides[parameter.Name] = converted;
        }

        return true;
    }

    private static bool TryConvert(string rawValue, Type targetType, out object? converted)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType == typeof(int) && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            converted = intValue;
            return true;
        }

        if (underlyingType == typeof(decimal) && decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
        {
            converted = decimalValue;
            return true;
        }

        if (underlyingType == typeof(bool) && bool.TryParse(rawValue, out var boolValue))
        {
            converted = boolValue;
            return true;
        }

        if (underlyingType.IsEnum && Enum.TryParse(underlyingType, rawValue, ignoreCase: true, out var enumValue))
        {
            converted = enumValue;
            return true;
        }

        if (underlyingType == typeof(string))
        {
            converted = rawValue;
            return true;
        }

        converted = null;
        return false;
    }
}
