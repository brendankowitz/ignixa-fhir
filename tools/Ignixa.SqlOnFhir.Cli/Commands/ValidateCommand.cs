using System.CommandLine;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.SqlOnFhir.Cli.Batch;
using Ignixa.SqlOnFhir.Parsing;

namespace Ignixa.SqlOnFhir.Cli.Commands;

internal static class ValidateCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var cmd = new Command("validate", "Validate ViewDefinition file(s).");

        var viewsOpt   = new Option<string>("--views")   { Description = "ViewDefinition file or directory", Required = true };
        var patternOpt = new Option<string>("--pattern") { Description = "ViewDefinition glob, dir mode only (default: **/*.json)", DefaultValueFactory = _ => "**/*.json" };

        cmd.Options.Add(viewsOpt);
        cmd.Options.Add(patternOpt);

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var views   = parseResult.GetValue(viewsOpt)!;
            var pattern = parseResult.GetValue(patternOpt)!;

            if (Directory.Exists(views))
                await ValidateDir(views, pattern);
            else
                await ValidateSingle(views);
        });

        return cmd;
    }

    private static async Task ValidateSingle(string viewsPath)
    {
        if (!File.Exists(viewsPath)) { Console.WriteLine($"✗ ViewDefinition not found: {viewsPath}"); Environment.ExitCode = 1; return; }

        var (valid, message, info) = await Validate(viewsPath);
        if (valid)
        {
            Console.WriteLine("✓ Valid JSON format");
            Console.WriteLine("✓ Resource type is ViewDefinition");
            Console.WriteLine("✓ ViewDefinition parsed successfully");
            Console.WriteLine($"  {info}");
            Console.WriteLine();
            Console.WriteLine("✓ ViewDefinition is valid");
        }
        else
        {
            Console.WriteLine($"✗ {message}");
            Environment.ExitCode = 1;
        }
    }

    private static async Task ValidateDir(string viewsDir, string pattern)
    {
        var viewFiles = BatchProcessor.DiscoverViewDefinitions(viewsDir, pattern).ToList();
        if (viewFiles.Count == 0) { Console.WriteLine($"✗ No ViewDefinition files found in {viewsDir}"); Environment.ExitCode = 1; return; }

        Console.WriteLine($"Validating {viewFiles.Count} ViewDefinition(s)\n");

        var results = new List<(string Name, bool Valid, string Detail)>();
        foreach (var vdPath in viewFiles)
        {
            var (valid, message, info) = await Validate(vdPath);
            results.Add((Path.GetFileName(vdPath), valid, valid ? info : message ?? string.Empty));
        }

        var nameWidth   = Math.Max(results.Max(r => r.Name.Length),   4);
        var detailWidth = Math.Max(results.Max(r => r.Detail.Length), 6);
        Console.WriteLine($"  {"Name".PadRight(nameWidth)}  Status  {"Detail".PadRight(detailWidth)}");
        Console.WriteLine($"  {new string('-', nameWidth)}  ------  {new string('-', detailWidth)}");
        foreach (var (name, valid, detail) in results)
            Console.WriteLine($"  {name.PadRight(nameWidth)}  {(valid ? "  ✓  " : "  ✗  ")}  {detail}");

        Console.WriteLine();
        var passed = results.Count(r => r.Valid);
        var failed = results.Count(r => !r.Valid);
        Console.WriteLine($"{(failed > 0 ? "✗" : "✓")} {passed} passed, {failed} failed");

        if (failed > 0 || passed == 0)
            Environment.ExitCode = 1;
    }

    private static async Task<(bool Valid, string? Message, string Info)> Validate(string vdPath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(vdPath);
            var node = JsonSourceNodeFactory.Parse(json);
            if (node is null) return (false, "Failed to parse JSON", string.Empty);

            var nav          = node.ToSourceNavigator();
            var resourceType = node.ResourceType;
            if (resourceType != "ViewDefinition")
                return (false, $"Not a ViewDefinition (found: {(string.IsNullOrEmpty(resourceType) ? "null" : resourceType)})", string.Empty);

            var viewDef   = ViewDefinitionExpressionParser.Parse(nav);
            var totalCols = viewDef.Select.Sum(s => s.Columns.Length);
            return (true, null, $"resource={viewDef.Resource}  columns={totalCols}  selects={viewDef.Select.Length}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message, string.Empty);
        }
    }
}
