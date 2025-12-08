using System.CommandLine;
using Ignixa.FhirMappingLanguage.Parser;
using Superpower.Model;

namespace Ignixa.Fml.Cli.Commands;

/// <summary>
/// Command for validating FHIR mapping files.
/// </summary>
public static class ValidateCommand
{
    public static Command Create()
    {
        var validateCommand = new Command("validate", "Validate a FHIR mapping file (compile check)");

        var mapOption = new Option<string>("--map", "Path to the mapping file (.map or .json StructureMap)") { IsRequired = true };
        var contextOption = new Option<string?>("--context", "Directory containing custom StructureDefinitions/ValueSets");

        validateCommand.AddOption(mapOption);
        validateCommand.AddOption(contextOption);

        validateCommand.SetHandler(async (map, context) =>
        {
            await HandleValidateCommand(map, context);
        }, mapOption, contextOption);

        return validateCommand;
    }

    private static async Task HandleValidateCommand(
        string mapPath,
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

            Console.WriteLine($"📖 Validating mapping from {mapPath}...");
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

            // Load and parse the mapping
            var mappingText = await File.ReadAllTextAsync(mapPath);
            var parser = new MappingParser();
            var map = parser.Parse(mappingText);

            // Display validation results
            Console.WriteLine("✓ Mapping validation successful!");
            Console.WriteLine();
            Console.WriteLine("📋 Mapping Details:");
            Console.WriteLine($"   URL: {map.Url}");
            Console.WriteLine($"   Identifier: {map.Identifier}");
            Console.WriteLine();

            // Display uses declarations
            if (map.Uses.Count > 0)
            {
                Console.WriteLine("📦 Uses Declarations:");
                foreach (var use in map.Uses)
                {
                    Console.WriteLine($"   {use.Mode,10} | {use.Alias,-15} | {use.Url}");
                }
                Console.WriteLine();
            }

            // Display imports
            if (map.Imports.Count > 0)
            {
                Console.WriteLine("📥 Imports:");
                foreach (var import in map.Imports)
                {
                    Console.WriteLine($"   {import}");
                }
                Console.WriteLine();
            }

            // Display groups
            if (map.Groups.Count > 0)
            {
                Console.WriteLine("📊 Groups:");
                foreach (var group in map.Groups)
                {
                    var paramList = string.Join(", ", group.Parameters.Select(p => 
                        $"{p.Mode} {p.Name} : {p.Type}"));
                    
                    Console.WriteLine($"   {group.Name}({paramList})");
                    
                    if (!string.IsNullOrEmpty(group.Extends))
                    {
                        Console.WriteLine($"      extends {group.Extends}");
                    }
                    
                    Console.WriteLine($"      {group.Rules.Count} rule(s)");
                }
                Console.WriteLine();
            }

            Console.WriteLine("✓ All checks passed!");
        }
        catch (ParseException ex)
        {
            Console.WriteLine("✗ Validation failed!");
            Console.WriteLine();
            Console.WriteLine($"Parse Error: {ex.Message}");
            
            if (ex.Position.Line != Position.Zero.Line || ex.Position.Column != Position.Zero.Column)
            {
                Console.WriteLine($"Location: Line {ex.Position.Line}, Column {ex.Position.Column}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("✗ Validation failed!");
            Console.WriteLine();
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
