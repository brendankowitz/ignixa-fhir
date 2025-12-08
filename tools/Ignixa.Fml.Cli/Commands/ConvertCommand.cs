using System.CommandLine;
using System.Text.Json;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Fml.Cli.Helpers;

namespace Ignixa.Fml.Cli.Commands;

/// <summary>
/// Command for converting FHIR resources using FML mappings.
/// </summary>
public static class ConvertCommand
{
    public static Command Create()
    {
        var convertCommand = new Command("convert", "Execute a FHIR mapping to transform input resources");

        var mapOption = new Option<string>("--map", "Path to the mapping file (.map or .json StructureMap)") { IsRequired = true };
        var inputOption = new Option<string>("--input", "Path to the input file, directory, or NDJSON file") { IsRequired = true };
        var outOption = new Option<string>("--out", "Path to the output file or directory") { IsRequired = true };
        var contextOption = new Option<string?>("--context", "Directory containing custom StructureDefinitions/ValueSets");
        var formatOption = new Option<string>("--format", () => "json", "Output format (json or xml)");

        convertCommand.AddOption(mapOption);
        convertCommand.AddOption(inputOption);
        convertCommand.AddOption(outOption);
        convertCommand.AddOption(contextOption);
        convertCommand.AddOption(formatOption);

        convertCommand.SetHandler(async (map, input, output, context, format) =>
        {
            await HandleConvertCommand(map, input, output, context, format);
        }, mapOption, inputOption, outOption, contextOption, formatOption);

        return convertCommand;
    }

    private static async Task HandleConvertCommand(
        string mapPath,
        string inputPath,
        string outputPath,
        string? contextPath,
        string format)
    {
        try
        {
            // Validate format
            if (!format.Equals("json", StringComparison.OrdinalIgnoreCase) && 
                !format.Equals("xml", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"✗ Error: Unsupported format '{format}'. Use 'json' or 'xml'.");
                return;
            }

            // Check if map file exists
            if (!File.Exists(mapPath))
            {
                Console.WriteLine($"✗ Error: Map file not found: {mapPath}");
                return;
            }

            // Load and parse the mapping
            Console.WriteLine($"📖 Loading mapping from {mapPath}...");
            var mappingText = await File.ReadAllTextAsync(mapPath);
            var parser = new MappingParser();
            var map = parser.Parse(mappingText);
            Console.WriteLine($"✓ Mapping '{map.Identifier}' loaded successfully");

            // TODO: Load context definitions if provided
            if (!string.IsNullOrEmpty(contextPath))
            {
                if (!Directory.Exists(contextPath))
                {
                    Console.WriteLine($"⚠ Warning: Context directory not found: {contextPath}");
                }
                else
                {
                    Console.WriteLine($"📂 Loading context from {contextPath}...");
                    // Context loading will be implemented in future iterations
                }
            }

            // Determine input type (file, directory, or NDJSON)
            if (File.Exists(inputPath))
            {
                if (inputPath.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessNdJsonFile(map, inputPath, outputPath, format);
                }
                else
                {
                    await ProcessSingleFile(map, inputPath, outputPath, format);
                }
            }
            else if (Directory.Exists(inputPath))
            {
                await ProcessDirectory(map, inputPath, outputPath, format);
            }
            else
            {
                Console.WriteLine($"✗ Error: Input path not found: {inputPath}");
            }
        }
        catch (ParseException ex)
        {
            Console.WriteLine($"✗ Error parsing mapping: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
        }
    }

    private static async Task ProcessSingleFile(
        Ignixa.FhirMappingLanguage.Expressions.MapExpression map,
        string inputPath,
        string outputPath,
        string format)
    {
        Console.WriteLine($"📄 Processing {inputPath}...");

        // Read input file
        var inputJson = await File.ReadAllTextAsync(inputPath);
        var inputNode = JsonSerializer.Deserialize<JsonElement>(inputJson);

        // Execute mapping
        var evaluator = new MappingEvaluator();
        var context = new MappingContext();
        
        // Set the source resource
        var sourceNode = JsonSourceNodeFactory.Parse(inputJson);
        context.SetSource("src", new SimpleElement(sourceNode, "src"));

        // Create target
        var targetNode = new ResourceJsonNode();
        context.SetTarget("tgt", new SimpleElement(targetNode, "tgt"));

        evaluator.Execute(map, context);

        // Write output
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }
        var outputJson = JsonSerializer.Serialize(targetNode.MutableNode, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, outputJson);

        Console.WriteLine($"✓ Output written to {outputPath}");
    }

    private static async Task ProcessDirectory(
        Ignixa.FhirMappingLanguage.Expressions.MapExpression map,
        string inputDir,
        string outputDir,
        string format)
    {
        Console.WriteLine($"📂 Processing directory {inputDir}...");

        // Create output directory
        Directory.CreateDirectory(outputDir);

        // Find all JSON files
        var jsonFiles = Directory.GetFiles(inputDir, "*.json", SearchOption.TopDirectoryOnly);
        
        if (jsonFiles.Length == 0)
        {
            Console.WriteLine("⚠ Warning: No JSON files found in input directory");
            return;
        }

        var successCount = 0;
        var errorCount = 0;

        foreach (var file in jsonFiles)
        {
            try
            {
                var fileName = Path.GetFileName(file);
                var outputPath = Path.Combine(outputDir, $"transformed-{fileName}");
                
                await ProcessSingleFile(map, file, outputPath, format);
                successCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error processing {Path.GetFileName(file)}: {ex.Message}");
                errorCount++;
            }
        }

        Console.WriteLine($"\n📊 Summary: {successCount} successful, {errorCount} errors");
    }

    private static async Task ProcessNdJsonFile(
        Ignixa.FhirMappingLanguage.Expressions.MapExpression map,
        string inputPath,
        string outputPath,
        string format)
    {
        Console.WriteLine($"📋 Processing NDJSON file {inputPath}...");

        // Create output directory
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var lines = await File.ReadAllLinesAsync(inputPath);
        var successCount = 0;
        var errorCount = 0;

        // Determine if we should write to a single NDJSON file or multiple files
        var isSingleOutput = outputPath.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase);
        List<string> outputLines = new();

        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            try
            {
                var inputNode = JsonSerializer.Deserialize<JsonElement>(lines[i]);
                
                // Execute mapping
                var evaluator = new MappingEvaluator();
                var context = new MappingContext();
                
                var sourceNode = JsonSourceNodeFactory.Parse(lines[i]);
                context.SetSource("src", new SimpleElement(sourceNode, "src"));
                
                var targetNode = new ResourceJsonNode();
                context.SetTarget("tgt", new SimpleElement(targetNode, "tgt"));

                evaluator.Execute(map, context);

                // Write output
                var outputJson = JsonSerializer.Serialize(targetNode.MutableNode, new JsonSerializerOptions { WriteIndented = !isSingleOutput });

                if (isSingleOutput)
                {
                    // Compact JSON for NDJSON format
                    outputJson = JsonSerializer.Serialize(targetNode.MutableNode);
                    outputLines.Add(outputJson);
                }
                else
                {
                    var outputFileDir = Path.GetDirectoryName(outputPath);
                    var singleOutputPath = Path.Combine(
                        string.IsNullOrEmpty(outputFileDir) ? "." : outputFileDir,
                        $"{Path.GetFileNameWithoutExtension(outputPath)}-{i:D4}.json");
                    await File.WriteAllTextAsync(singleOutputPath, outputJson);
                }

                successCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error processing line {i + 1}: {ex.Message}");
                errorCount++;
            }
        }

        if (isSingleOutput && outputLines.Count > 0)
        {
            await File.WriteAllLinesAsync(outputPath, outputLines);
            Console.WriteLine($"✓ Output written to {outputPath}");
        }

        Console.WriteLine($"\n📊 Summary: {successCount} successful, {errorCount} errors");
    }
}
