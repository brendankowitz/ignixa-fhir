using System.CommandLine;
using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.SqlOnFhir.Cli.Batch;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Parsing;

namespace Ignixa.SqlOnFhir.Cli.Commands;

internal static class PreviewCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var cmd = new Command("preview", "Preview schema and sample rows. Omit --input for schema-only mode.");

        var viewsOpt   = new Option<string>("--views")   { Description = "ViewDefinition file or directory", Required = true };
        var inputOpt   = new Option<string?>("--input")  { Description = "NDJSON file or directory (optional; omit for schema-only)" };
        var rowsOpt    = new Option<int>("--rows")       { Description = "Max sample rows per ViewDefinition (default: 5)", DefaultValueFactory = _ => 5 };
        var patternOpt = new Option<string>("--pattern") { Description = "ViewDefinition glob, dir mode only (default: **/*.json)", DefaultValueFactory = _ => "**/*.json" };
        var varOpt     = new Option<string[]>("--var")   { Description = "FHIRPath variable name=value, repeatable", AllowMultipleArgumentsPerToken = false };

        cmd.Options.Add(viewsOpt);
        cmd.Options.Add(inputOpt);
        cmd.Options.Add(rowsOpt);
        cmd.Options.Add(patternOpt);
        cmd.Options.Add(varOpt);

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var views   = parseResult.GetValue(viewsOpt)!;
            var input   = parseResult.GetValue(inputOpt);
            var rows    = parseResult.GetValue(rowsOpt);
            var pattern = parseResult.GetValue(patternOpt)!;
            var vars    = VarParser.Parse(parseResult.GetValue(varOpt));

            if (Directory.Exists(views))
                await PreviewDir(schemaProvider, fhirVersion, views, input, rows, pattern, vars);
            else
                await PreviewSingle(schemaProvider, fhirVersion, views, input, rows, vars);
        });

        return cmd;
    }

    private static async Task PreviewSingle(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string viewsPath,
        string? inputPath,
        int maxRows,
        IReadOnlyDictionary<string, string> vars)
    {
        if (!File.Exists(viewsPath)) { Console.WriteLine($"✗ ViewDefinition not found: {viewsPath}"); Environment.ExitCode = 1; return; }
        if (inputPath is not null && !File.Exists(inputPath)) { Console.WriteLine($"✗ Input not found: {inputPath}"); Environment.ExitCode = 1; return; }

        var viewNav = ParseViewDefinition(viewsPath);
        if (viewNav is null) { Console.WriteLine("✗ Failed to parse ViewDefinition"); Environment.ExitCode = 1; return; }

        PrintSchema(viewNav, fhirVersion);

        if (inputPath is not null)
            await PrintSampleRows(inputPath, viewNav, schemaProvider, maxRows, vars);
    }

    private static async Task PreviewDir(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string viewsDir,
        string? inputDir,
        int maxRows,
        string pattern,
        IReadOnlyDictionary<string, string> vars)
    {
        var viewFiles = BatchProcessor.DiscoverViewDefinitions(viewsDir, pattern).ToList();
        if (viewFiles.Count == 0) { Console.WriteLine($"✗ No ViewDefinition files found in {viewsDir}"); Environment.ExitCode = 1; return; }

        Console.WriteLine($"Found {viewFiles.Count} ViewDefinition(s)\n");

        foreach (var vdPath in viewFiles)
        {
            Console.WriteLine($"── {Path.GetFileName(vdPath)} ────────────────────────────────────");
            var viewNav = ParseViewDefinition(vdPath);
            if (viewNav is null) { Console.WriteLine("  ✗ Parse error\n"); continue; }

            PrintSchema(viewNav, fhirVersion);

            if (inputDir is not null && Directory.Exists(inputDir))
            {
                var resource   = viewNav.Children("resource").FirstOrDefault()?.Text ?? string.Empty;
                var inputFiles = BatchProcessor.FindInputFiles(inputDir, resource, "*{resource}*.ndjson").ToList();
                if (inputFiles.Count > 0)
                    await PrintSampleRows(inputFiles[0], viewNav, schemaProvider, maxRows, vars);
                else
                    Console.WriteLine($"  (no matching NDJSON for '{resource}')");
            }

            Console.WriteLine();
        }
    }

    private static void PrintSchema(ISourceNavigator viewNav, string fhirVersion)
    {
        Console.WriteLine();
        Console.WriteLine("=== Schema ===");
        Console.WriteLine();

        try
        {
            var viewExpr   = ViewDefinitionExpressionParser.Parse(viewNav);
            var schemaEval = new SqlOnFhirSchemaEvaluator();
            var columns    = schemaEval.GetSchema(viewExpr);

            if (columns.Count > 0)
            {
                var maxLen = columns.Max(c => c.Name.Length);
                foreach (var col in columns.OrderBy(c => c.Name))
                    Console.WriteLine($"  {col.Name.PadRight(maxLen)}  {col.Type ?? "inferred"}{(col.Collection ? " (collection)" : "")}");
            }

            Console.WriteLine();
            Console.WriteLine($"Resource: {viewExpr.Resource}  |  FHIR: {fhirVersion.ToUpperInvariant()}  |  Columns: {columns.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Schema extraction failed: {ex.Message}");
        }
    }

    private static async Task PrintSampleRows(
        string inputPath,
        ISourceNavigator viewNav,
        IFhirSchemaProvider schemaProvider,
        int maxRows,
        IReadOnlyDictionary<string, string> vars)
    {
        var evaluator  = new SqlOnFhirEvaluator();
        var sampleRows = new List<Dictionary<string, object?>>();
        var reachedMax = false;

        await foreach (var line in File.ReadLinesAsync(inputPath))
        {
            if (reachedMax) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var node = JsonSourceNodeFactory.Parse(line);
            if (node is null) continue;
            var element = node.ToElement(schemaProvider);
            var rows = evaluator.Evaluate(viewNav, element, vars);
            if (rows is null) continue;
            foreach (var row in rows)
            {
                sampleRows.Add(row);
                if (sampleRows.Count >= maxRows) { reachedMax = true; break; }
            }
        }

        if (sampleRows.Count == 0) { Console.WriteLine("\n(no rows generated)"); return; }

        Console.WriteLine();
        Console.WriteLine($"=== Sample Rows ({sampleRows.Count}) ===");
        Console.WriteLine();

        var viewExpr   = ViewDefinitionExpressionParser.Parse(viewNav);
        var schemaEval = new SqlOnFhirSchemaEvaluator();
        var columns    = schemaEval.GetSchema(viewExpr).OrderBy(c => c.Name).Select(c => c.Name).ToList();
        DisplayTable(sampleRows, columns);
        Console.WriteLine($"\n✓ Preview completed with {sampleRows.Count} sample row(s)");
    }

    private static void DisplayTable(List<Dictionary<string, object?>> rows, List<string> columns)
    {
        var widths = columns.ToDictionary(c => c, c =>
        {
            var max = c.Length;
            foreach (var row in rows)
                if (row.TryGetValue(c, out var v) && v != null)
                    max = Math.Max(max, FormatValue(v).Length);
            return Math.Min(max, 50);
        });

        var header = string.Join(" | ", columns.Select(c => c.PadRight(widths[c])));
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));
        foreach (var row in rows)
        {
            var cells = columns.Select(c =>
            {
                if (row.TryGetValue(c, out var v) && v != null)
                {
                    var s = FormatValue(v);
                    if (s.Length > widths[c]) s = string.Concat(s.AsSpan(0, widths[c] - 3), "...");
                    return s.PadRight(widths[c]);
                }
                return string.Empty.PadRight(widths[c]);
            });
            Console.WriteLine(string.Join(" | ", cells));
        }
    }

    private static string FormatValue(object value) => value switch
    {
        DateTime dt        => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        decimal d          => d.ToString("0.##", CultureInfo.InvariantCulture),
        double dbl         => dbl.ToString("0.##", CultureInfo.InvariantCulture),
        float f            => f.ToString("0.##", CultureInfo.InvariantCulture),
        bool b             => b ? "true" : "false",
        _                  => value.ToString() ?? string.Empty
    };

    private static ISourceNavigator? ParseViewDefinition(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSourceNodeFactory.Parse(json)?.ToSourceNavigator();
    }
}
