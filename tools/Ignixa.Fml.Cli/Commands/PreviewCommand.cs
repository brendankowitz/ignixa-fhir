using System.CommandLine;
using System.Text.Json;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Fml.Cli.Helpers;

namespace Ignixa.Fml.Cli.Commands;

/// <summary>
/// Command for previewing FHIR mapping transformations to console.
/// </summary>
public static class PreviewCommand
{
    public static Command Create()
    {
        var previewCommand = new Command("preview", "Preview mapping transformation result in console");

        var mapOption = new Option<string>("--map", "Path to the mapping file (.map or .json StructureMap)") { IsRequired = true };
        var inputOption = new Option<string>("--input", "Path to the input file") { IsRequired = true };
        var contextOption = new Option<string?>("--context", "Directory containing custom StructureDefinitions/ValueSets");

        previewCommand.AddOption(mapOption);
        previewCommand.AddOption(inputOption);
        previewCommand.AddOption(contextOption);

        previewCommand.SetHandler(async (map, input, context) =>
        {
            await HandlePreviewCommand(map, input, context);
        }, mapOption, inputOption, contextOption);

        return previewCommand;
    }

    private static async Task HandlePreviewCommand(
        string mapPath,
        string inputPath,
        string? contextPath)
    {
        try
        {
            // Check if map file exists
            if (!File.Exists(mapPath))
            {
                Console.WriteLine($"✗ Error: Map file not found: {mapPath}");
                return;
            }

            // Check if input file exists
            if (!File.Exists(inputPath))
            {
                Console.WriteLine($"✗ Error: Input file not found: {inputPath}");
                return;
            }

            // Load and parse the mapping
            Console.WriteLine($"📖 Loading mapping from {mapPath}...");
            var mappingText = await File.ReadAllTextAsync(mapPath);
            var parser = new MappingParser();
            var map = parser.Parse(mappingText);
            Console.WriteLine($"✓ Mapping '{map.Identifier}' loaded successfully");
            Console.WriteLine();

            // TODO: Load context definitions if provided
            if (!string.IsNullOrEmpty(contextPath))
            {
                if (!Directory.Exists(contextPath))
                {
                    Console.WriteLine($"⚠ Warning: Context directory not found: {contextPath}");
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine($"📂 Loading context from {contextPath}...");
                    // Context loading will be implemented in future iterations
                    Console.WriteLine();
                }
            }

            // Read input file
            Console.WriteLine($"📄 Reading input from {inputPath}...");
            var inputJson = await File.ReadAllTextAsync(inputPath);
            Console.WriteLine("✓ Input loaded");
            Console.WriteLine();

            // Execute mapping
            Console.WriteLine("🔄 Executing mapping...");
            var evaluator = new MappingEvaluator();
            var context = new MappingContext();
            
            // Set the source resource
            var sourceNode = JsonSourceNodeFactory.Parse(inputJson);
            context.SetSource("src", new SimpleElement(sourceNode, "src"));

            // Create target
            var targetNode = new ResourceJsonNode();
            context.SetTarget("tgt", new SimpleElement(targetNode, "tgt"));

            evaluator.Execute(map, context);
            Console.WriteLine("✓ Mapping executed successfully");
            Console.WriteLine();

            // Display result
            Console.WriteLine("📋 Transformation Result:");
            Console.WriteLine("─────────────────────────────────────────────────────────────");
            var outputJson = JsonSerializer.Serialize(
                targetNode.MutableNode,
                new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(outputJson);
            Console.WriteLine("─────────────────────────────────────────────────────────────");
        }
        catch (ParseException ex)
        {
            Console.WriteLine($"✗ Error parsing mapping: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }
        }
    }
}
