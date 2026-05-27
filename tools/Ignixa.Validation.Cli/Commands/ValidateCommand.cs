using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Validation.Cli.Commands;

/// <summary>
/// Command for validating FHIR resources.
/// </summary>
internal static class ValidateCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var command = new Command(fhirVersion, $"Validate using FHIR {fhirVersion.ToUpperInvariant()} specification");

        var inputOption = new Option<string?>("--input") { Description = "Path to JSON file to validate" };
        var jsonOption = new Option<string?>("--json") { Description = "JSON string to validate" };
        var outOption = new Option<string?>("--out") { Description = "Output file for validation results (OperationOutcome JSON)" };
        var consoleOption = new Option<bool>("--console") { Description = "Display formatted validation results in console", DefaultValueFactory = _ => false };
        var depthOption = new Option<string>("--depth")
        {
            Description = "Validation depth: minimal | spec | full | compatibility (default: spec)",
            DefaultValueFactory = _ => "spec",
        };
        var packageOption = new Option<string[]>("--package")
        {
            Description = "FHIR IG package to layer for profile validation, in form 'id@version' (repeatable). Example: --package hl7.fhir.us.core@6.1.0",
            AllowMultipleArgumentsPerToken = true,
        };

        command.Options.Add(inputOption);
        command.Options.Add(jsonOption);
        command.Options.Add(outOption);
        command.Options.Add(consoleOption);
        command.Options.Add(depthOption);
        command.Options.Add(packageOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var input = parseResult.GetValue(inputOption);
            var json = parseResult.GetValue(jsonOption);
            var output = parseResult.GetValue(outOption);
            var console = parseResult.GetValue(consoleOption);
            var depth = parseResult.GetValue(depthOption) ?? "spec";
            var packages = parseResult.GetValue(packageOption) ?? Array.Empty<string>();
            await HandleValidateCommand(schemaProvider, fhirVersion, input, json, output, console, depth, packages, cancellationToken);
        });

        return command;
    }

    private static async Task HandleValidateCommand(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string? inputFile,
        string? jsonString,
        string? outputFile,
        bool consoleOutput,
        string depthName,
        string[] packageSpecs,
        CancellationToken cancellationToken)
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
                jsonContent = await File.ReadAllTextAsync(inputFile, cancellationToken);
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

            // Parse depth (case-insensitive)
            if (!Enum.TryParse<ValidationDepth>(depthName, ignoreCase: true, out var depth))
            {
                Console.WriteLine($"✗ Error: Unknown --depth value '{depthName}'. Use minimal, spec, full, or compatibility.");
                Environment.ExitCode = 1;
                return;
            }

            // Build the schema chain. When --package was supplied, layer each package's
            // StructureDefinitions and ValueSets on top of the base spec, and use the
            // profile-aware resolver so meta.profile composes the right checks.
            ISchema effectiveSchema = schemaProvider;
            ITerminologyService terminology = new InMemoryTerminologyService(schemaProvider.ValueSetProvider);
            if (packageSpecs.Length > 0)
            {
                Console.WriteLine($"→ Loading {packageSpecs.Length} IG package(s)...");
                var packageResources = new List<Ignixa.PackageManagement.Models.ExtractedResource>();
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                var cacheDir = Path.Combine(Path.GetTempPath(), "ignixa-validator-package-cache");
                Directory.CreateDirectory(cacheDir);
                var cache = new PackageCacheManager(cacheDir, NullLogger<PackageCacheManager>.Instance);
                var pkgLoader = new NpmPackageLoader(httpClient, cache, options: null, NullLogger<NpmPackageLoader>.Instance);
                var extractor = new PackageExtractor(NullLogger<PackageExtractor>.Instance);

                foreach (var spec in packageSpecs)
                {
                    var (pkgId, pkgVer) = SplitPackageSpec(spec);
                    if (pkgId is null || pkgVer is null)
                    {
                        Console.WriteLine($"✗ Error: Invalid --package value '{spec}'. Expected 'id@version'.");
                        Environment.ExitCode = 1;
                        return;
                    }
                    try
                    {
                        Console.WriteLine($"  • {pkgId}@{pkgVer}");
                        await using var stream = await pkgLoader.DownloadPackageAsync(pkgId, pkgVer, cancellationToken);
                        var extracted = await extractor.ExtractAsync(stream, cancellationToken);
                        packageResources.AddRange(extracted.Resources);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"✗ Error loading package '{spec}': {ex.Message}");
                        Environment.ExitCode = 1;
                        return;
                    }
                }

                effectiveSchema = new ProfileLayeredSchemaProvider(schemaProvider, packageResources);
                var packageVs = new PackageValueSetSource(packageResources);
                terminology = new InMemoryTerminologyService(
                    primary: schemaProvider.ValueSetProvider,
                    additional: new[] { (IValueSetProvider)packageVs });
            }

            // Resolve validation schema. Always use the profile-aware resolver so meta.profile
            // is honored regardless of whether --package was passed.
            var innerResolver = new StructureDefinitionSchemaResolver(effectiveSchema, terminologyService: terminology);
            var cachedResolver = new CachedValidationSchemaResolver(innerResolver);
            var profileAwareResolver = new ProfileAwareValidationSchemaResolver(cachedResolver);

            var element = sourceNode.ToElement(effectiveSchema);
            var schema = profileAwareResolver.ResolveForElement(element);

            if (schema == null)
            {
                Console.WriteLine($"✗ Error: Validation schema not found for resource type '{resourceType}'");
                Environment.ExitCode = 1;
                return;
            }

            // Perform validation
            var settings = new ValidationSettings { Depth = depth };
            var state = new ValidationState();
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

    internal static (string? Id, string? Version) SplitPackageSpec(string spec)
    {
        var atIndex = spec.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == spec.Length - 1)
        {
            return (null, null);
        }
        return (spec[..atIndex], spec[(atIndex + 1)..]);
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
