using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;

namespace Ignixa.Validation.Cli.Commands;

/// <summary>
/// Command for validating FHIR resources.
/// </summary>
internal static class ValidateCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var command = new Command("validate", "Validate a FHIR resource");

        var inputOption = new Option<string?>("--input", "Path to JSON file to validate");
        var jsonOption = new Option<string?>("--json", "JSON string to validate");
        var outOption = new Option<string?>("--out", "Output file for validation results (OperationOutcome JSON)");
        var consoleOption = new Option<bool>("--console", () => false, "Display formatted validation results in console");

        command.AddOption(inputOption);
        command.AddOption(jsonOption);
        command.AddOption(outOption);
        command.AddOption(consoleOption);

        command.SetHandler(async (input, json, output, console) =>
        {
            await HandleValidateCommand(schemaProvider, fhirVersion, input, json, output, console);
        }, inputOption, jsonOption, outOption, consoleOption);

        return command;
    }

    private static async Task HandleValidateCommand(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string? inputFile,
        string? jsonString,
        string? outputFile,
        bool consoleOutput)
    {
        try
        {
            // Validate input options
            if (string.IsNullOrEmpty(inputFile) && string.IsNullOrEmpty(jsonString))
            {
                Console.WriteLine("✗ Error: Either --input or --json must be specified");
                Environment.ExitCode = 1;
                return;
            }

            if (!string.IsNullOrEmpty(inputFile) && !string.IsNullOrEmpty(jsonString))
            {
                Console.WriteLine("✗ Error: Cannot specify both --input and --json");
                Environment.ExitCode = 1;
                return;
            }

            if (!consoleOutput && string.IsNullOrEmpty(outputFile))
            {
                Console.WriteLine("✗ Error: Either --console or --out must be specified");
                Environment.ExitCode = 1;
                return;
            }

            // Read JSON content
            string jsonContent;
            if (!string.IsNullOrEmpty(inputFile))
            {
                if (!File.Exists(inputFile))
                {
                    Console.WriteLine($"✗ Error: File not found: {inputFile}");
                    Environment.ExitCode = 1;
                    return;
                }
                jsonContent = await File.ReadAllTextAsync(inputFile);
            }
            else
            {
                jsonContent = jsonString!;
            }

            // Parse JSON
            JsonNode? jsonNode;
            try
            {
                jsonNode = JsonNode.Parse(jsonContent);
                if (jsonNode == null)
                {
                    Console.WriteLine("✗ Error: Invalid JSON - parsed to null");
                    Environment.ExitCode = 1;
                    return;
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"✗ Error: Invalid JSON - {ex.Message}");
                Environment.ExitCode = 1;
                return;
            }

            // Create source node
            var sourceNode = JsonNodeSourceNode.Create(jsonNode);
            var resourceType = sourceNode.ResourceType ?? sourceNode.Name;

            if (string.IsNullOrEmpty(resourceType))
            {
                Console.WriteLine("✗ Error: Could not determine resource type from JSON");
                Environment.ExitCode = 1;
                return;
            }

            // Get validation schema
            var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";
            var innerResolver = new StructureDefinitionSchemaResolver(schemaProvider);
            var schemaResolver = new CachedValidationSchemaResolver(innerResolver);
            var schema = schemaResolver.GetSchema(canonicalUrl);

            if (schema == null)
            {
                Console.WriteLine($"✗ Error: Validation schema not found for resource type '{resourceType}'");
                Environment.ExitCode = 1;
                return;
            }

            // Perform validation
            var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
            var state = new ValidationState();
            var element = sourceNode.ToElement(schemaProvider);
            var validationResult = schema.Validate(element, settings, state);

            // Output results
            if (consoleOutput)
            {
                DisplayConsoleOutput(validationResult, resourceType, fhirVersion);
            }

            if (!string.IsNullOrEmpty(outputFile))
            {
                await WriteOperationOutcomeToFile(validationResult, outputFile);
                Console.WriteLine($"✓ Validation results written to: {outputFile}");
            }

            // Exit with appropriate code
            Environment.ExitCode = validationResult.IsValid ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static void DisplayConsoleOutput(ValidationResult result, string resourceType, string fhirVersion)
    {
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  FHIR Validation Results ({fhirVersion.ToUpperInvariant()})");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  Resource Type: {resourceType}");
        Console.WriteLine($"  Status: {(result.IsValid ? "✓ VALID" : "✗ INVALID")}");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Count issues by severity
        var fatalCount = result.Issues.Count(i => i.Severity == IssueSeverity.Fatal);
        var errorCount = result.Issues.Count(i => i.Severity == IssueSeverity.Error);
        var warningCount = result.Issues.Count(i => i.Severity == IssueSeverity.Warning);
        var informationCount = result.Issues.Count(i => i.Severity == IssueSeverity.Information);

        Console.WriteLine("Issue Summary:");
        Console.WriteLine($"  Fatal:       {fatalCount,4}");
        Console.WriteLine($"  Error:       {errorCount,4}");
        Console.WriteLine($"  Warning:     {warningCount,4}");
        Console.WriteLine($"  Information: {informationCount,4}");
        Console.WriteLine($"  Total:       {result.Issues.Count,4}");
        Console.WriteLine();

        if (result.Issues.Any())
        {
            Console.WriteLine("───────────────────────────────────────────────────────────────");
            Console.WriteLine("Validation Issues:");
            Console.WriteLine("───────────────────────────────────────────────────────────────");
            Console.WriteLine();

            // Group by severity for better readability
            var issuesBySeverity = result.Issues
                .OrderBy(i => i.Severity)
                .ThenBy(i => i.Path);

            foreach (var issue in issuesBySeverity)
            {
                var severityIcon = issue.Severity switch
                {
                    IssueSeverity.Fatal => "💀",
                    IssueSeverity.Error => "❌",
                    IssueSeverity.Warning => "⚠️ ",
                    IssueSeverity.Information => "ℹ️ ",
                    _ => "  "
                };

                var severityText = issue.Severity.ToString().ToUpperInvariant().PadRight(11);
                
                Console.WriteLine($"{severityIcon} {severityText} @ {issue.Path}");
                Console.WriteLine($"   {issue.Message}");
                
                if (issue.Details?.Text != null && issue.Details.Text != issue.Message)
                {
                    Console.WriteLine($"   Details: {issue.Details.Text}");
                }
                
                Console.WriteLine();
            }
        }
        else
        {
            Console.WriteLine("✓ No validation issues found!");
            Console.WriteLine();
        }

        Console.WriteLine("═══════════════════════════════════════════════════════════════");
    }

    private static async Task WriteOperationOutcomeToFile(ValidationResult result, string outputFile)
    {
        var operationOutcome = result.ToOperationOutcome();
        
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(operationOutcome.MutableNode, options);
        
        // Ensure output directory exists
        var directory = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputFile, json);
    }
}
