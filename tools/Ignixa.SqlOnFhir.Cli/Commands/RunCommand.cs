using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.SqlOnFhir.Cli.Batch;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Parsing;
using Ignixa.SqlOnFhir.Writers;
using Microsoft.Extensions.Logging.Abstractions;
using Parquet.Data;
using Parquet.Schema;

namespace Ignixa.SqlOnFhir.Cli.Commands;

internal static class RunCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var cmd = new Command("run", "Convert FHIR resources using ViewDefinition(s). Accepts file or directory for --views and --input.");

        var viewsOpt      = new Option<string>("--views")         { Description = "ViewDefinition file or directory",                    Required = true };
        var inputOpt      = new Option<string>("--input")         { Description = "NDJSON file or directory",                            Required = true };
        var outOpt        = new Option<string>("--out")           { Description = "Output file (single mode) or directory (batch mode)", Required = true };
        var formatOpt     = new Option<string>("--format")        { Description = "Batch mode output format: parquet, csv, ndjson (default: parquet)",          DefaultValueFactory = _ => "parquet" };
        var patternOpt    = new Option<string>("--pattern")       { Description = "ViewDefinition glob, batch mode only (default: **/*.json)",                  DefaultValueFactory = _ => "**/*.json" };
        var inputPatOpt   = new Option<string>("--input-pattern") { Description = "NDJSON match pattern, batch mode only (default: *{resource}*.ndjson)",       DefaultValueFactory = _ => "*{resource}*.ndjson" };
        var varOpt        = new Option<string[]>("--var")         { Description = "FHIRPath variable name=value, repeatable", AllowMultipleArgumentsPerToken = false };
        var quietOpt      = new Option<bool>("--quiet")           { Description = "Suppress all console output" };
        var statsOutOpt   = new Option<string?>("--stats-out")    { Description = "Write JSON stats summary to file" };

        cmd.Options.Add(viewsOpt);
        cmd.Options.Add(inputOpt);
        cmd.Options.Add(outOpt);
        cmd.Options.Add(formatOpt);
        cmd.Options.Add(patternOpt);
        cmd.Options.Add(inputPatOpt);
        cmd.Options.Add(varOpt);
        cmd.Options.Add(quietOpt);
        cmd.Options.Add(statsOutOpt);

        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var views        = parseResult.GetValue(viewsOpt)!;
            var input        = parseResult.GetValue(inputOpt)!;
            var out_         = parseResult.GetValue(outOpt)!;
            var format       = parseResult.GetValue(formatOpt)!;
            var pattern      = parseResult.GetValue(patternOpt)!;
            var inputPattern = parseResult.GetValue(inputPatOpt)!;
            var vars         = VarParser.Parse(parseResult.GetValue(varOpt));
            var quiet        = parseResult.GetValue(quietOpt);
            var statsOut     = parseResult.GetValue(statsOutOpt);

            if (Directory.Exists(views))
                await RunBatch(schemaProvider, fhirVersion, views, input, out_, format, pattern, inputPattern, vars, quiet, statsOut, cancellationToken);
            else
                await RunSingle(schemaProvider, fhirVersion, views, input, out_, vars, quiet, cancellationToken);
        });

        return cmd;
    }

    private static async Task RunSingle(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string viewsPath,
        string inputPath,
        string outputPath,
        IReadOnlyDictionary<string, string> vars,
        bool quiet,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var format = DetectFormat(outputPath);
            if (format is null) { Print(quiet, "✗ Unsupported output extension. Use .parquet, .csv, or .ndjson"); Environment.ExitCode = 1; return; }
            if (!File.Exists(viewsPath)) { Print(quiet, $"✗ ViewDefinition not found: {viewsPath}"); Environment.ExitCode = 1; return; }
            if (!File.Exists(inputPath)) { Print(quiet, $"✗ Input not found: {inputPath}"); Environment.ExitCode = 1; return; }

            var viewNav = ParseViewDefinition(viewsPath);
            if (viewNav is null) { Print(quiet, "✗ Failed to parse ViewDefinition"); Environment.ExitCode = 1; return; }

            EnsureParentDirectory(outputPath);

            var schemaEval = new SqlOnFhirSchemaEvaluator();
            var viewExpr   = ViewDefinitionExpressionParser.Parse(viewNav);
            var colSchemas = schemaEval.GetSchema(viewExpr);
            Print(quiet, $"✓ Using FHIR {fhirVersion.ToUpperInvariant()} — {colSchemas.Count} columns");

            var evaluator = new SqlOnFhirEvaluator();
            var rows = await WriteOutputAsync(outputPath, format, [inputPath], viewNav, colSchemas, schemaProvider, evaluator, vars, cancellationToken);
            var bytes = new FileInfo(outputPath).Exists ? new FileInfo(outputPath).Length : 0L;
            Print(quiet, $"✓ {rows:N0} rows → {outputPath} ({bytes:N0} bytes) in {sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            Print(quiet, $"✗ Error: {ex.Message}");
            await Console.Error.WriteLineAsync(ex.StackTrace);
            Environment.ExitCode = 1;
        }
    }

    private static async Task RunBatch(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string viewsDir,
        string inputDir,
        string outputDir,
        string format,
        string pattern,
        string inputPattern,
        IReadOnlyDictionary<string, string> vars,
        bool quiet,
        string? statsOut,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(inputDir)) { Print(quiet, $"✗ Input directory not found: {inputDir}"); Environment.ExitCode = 1; return; }

        Directory.CreateDirectory(outputDir);

        var viewFiles = BatchProcessor.DiscoverViewDefinitions(viewsDir, pattern).ToList();
        if (viewFiles.Count == 0) { Print(quiet, $"✗ No ViewDefinition files found matching '{pattern}' in {viewsDir}"); Environment.ExitCode = 1; return; }

        Print(quiet, $"✓ Found {viewFiles.Count} ViewDefinition(s)  [{fhirVersion.ToUpperInvariant()}]\n");

        var results   = new List<BatchViewResult>();
        var evaluator = new SqlOnFhirEvaluator();

        for (var i = 0; i < viewFiles.Count; i++)
        {
            var vdPath = viewFiles[i];
            var vdName = Path.GetFileNameWithoutExtension(vdPath);
            var sw     = Stopwatch.StartNew();

            var viewNav = ParseViewDefinition(vdPath);
            if (viewNav is null)
            {
                Print(quiet, $"  [{i + 1}/{viewFiles.Count}] {vdName}  → skipped (parse error)");
                results.Add(new BatchViewResult(vdName, BatchViewStatus.Skipped, SkipReason: "Failed to parse ViewDefinition JSON"));
                continue;
            }

            var resource = viewNav.Children("resource").FirstOrDefault()?.Text ?? string.Empty;
            if (string.IsNullOrEmpty(resource))
            {
                Print(quiet, $"  [{i + 1}/{viewFiles.Count}] {vdName}  → skipped (missing resource field)");
                results.Add(new BatchViewResult(vdName, BatchViewStatus.Skipped, SkipReason: "ViewDefinition missing 'resource' field"));
                continue;
            }

            var inputFiles = BatchProcessor.FindInputFiles(inputDir, resource, inputPattern).ToList();
            if (inputFiles.Count == 0)
            {
                Print(quiet, $"  [{i + 1}/{viewFiles.Count}] {vdName}  → skipped (no {resource} NDJSON in {inputDir})");
                results.Add(new BatchViewResult(vdName, BatchViewStatus.Skipped, SkipReason: $"No NDJSON files matching '{resource}'"));
                continue;
            }

            var outputPath = BatchProcessor.GetOutputPath(outputDir, vdPath, format);

            try
            {
                var schemaEval = new SqlOnFhirSchemaEvaluator();
                var viewExpr   = ViewDefinitionExpressionParser.Parse(viewNav);
                var colSchemas = schemaEval.GetSchema(viewExpr);
                var rows  = await WriteOutputAsync(outputPath, format, inputFiles, viewNav, colSchemas, schemaProvider, evaluator, vars, cancellationToken);
                var bytes = new FileInfo(outputPath).Exists ? new FileInfo(outputPath).Length : 0L;
                sw.Stop();

                Print(quiet, $"  [{i + 1}/{viewFiles.Count}] {vdName,-40}  {rows,8:N0} rows  {Path.GetFileName(outputPath)} ({bytes / 1_048_576.0:F1} MB)  {sw.Elapsed.TotalSeconds:F1}s");
                results.Add(new BatchViewResult(vdName, BatchViewStatus.Completed, rows, bytes, sw.Elapsed.TotalSeconds, outputPath));
            }
            catch (Exception ex)
            {
                Print(quiet, $"  [{i + 1}/{viewFiles.Count}] {vdName}  → ERROR: {ex.Message}");
                results.Add(new BatchViewResult(vdName, BatchViewStatus.Failed, ErrorMessage: ex.Message));
            }
        }

        var completed = results.Count(r => r.Status == BatchViewStatus.Completed);
        var skipped   = results.Count(r => r.Status == BatchViewStatus.Skipped);
        var failed    = results.Count(r => r.Status == BatchViewStatus.Failed);
        Print(quiet, $"\n✓ Done: {completed} completed, {skipped} skipped, {failed} failed");

        if (statsOut is not null)
        {
            var stats = new
            {
                total = results.Count,
                completed,
                skipped,
                failed,
                views = results.Select(r => new
                {
                    name            = r.ViewDefinitionName,
                    status          = r.Status switch { BatchViewStatus.Completed => "completed", BatchViewStatus.Skipped => "skipped", _ => "failed" },
                    rows            = r.RowsWritten,
                    bytes           = r.BytesWritten,
                    durationSeconds = r.DurationSeconds,
                    skipReason      = r.SkipReason,
                    errorMessage    = r.ErrorMessage
                })
            };
            await File.WriteAllTextAsync(statsOut,
                JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
        }

        if (completed == 0)
            Environment.ExitCode = 1;
    }

    private static async Task<long> WriteOutputAsync(
        string outputPath,
        string format,
        List<string> inputFiles,
        ISourceNavigator viewNav,
        IReadOnlyList<ColumnSchema> colSchemas,
        IFhirSchemaProvider schemaProvider,
        SqlOnFhirEvaluator evaluator,
        IReadOnlyDictionary<string, string> vars,
        CancellationToken cancellationToken)
    {
        var rows = 0L;
        if (format == "parquet")
        {
            var (schema, typeMap) = BuildParquetSchema(colSchemas);
            await using var writer = new ParquetFileWriter(outputPath, schema, NullLogger.Instance, typeMap);
            foreach (var file in inputFiles)
                await foreach (var row in StreamRows(file, viewNav, schemaProvider, evaluator, vars, cancellationToken))
                { await writer.WriteRowAsync(row, cancellationToken); rows++; }
            await writer.FlushAsync(cancellationToken);
        }
        else if (format == "csv")
        {
            await using var writer = new CsvFileWriter(outputPath, NullLogger.Instance);
            foreach (var file in inputFiles)
                await foreach (var row in StreamRows(file, viewNav, schemaProvider, evaluator, vars, cancellationToken))
                { await writer.WriteRowAsync(row, cancellationToken); rows++; }
            await writer.FlushAsync(cancellationToken);
        }
        else
        {
            await using var writer = new NdjsonFileWriter(outputPath, NullLogger.Instance);
            foreach (var file in inputFiles)
                await foreach (var row in StreamRows(file, viewNav, schemaProvider, evaluator, vars, cancellationToken))
                { await writer.WriteRowAsync(row, cancellationToken); rows++; }
            await writer.FlushAsync(cancellationToken);
        }
        return rows;
    }

    private static async IAsyncEnumerable<Dictionary<string, object?>> StreamRows(
        string inputPath,
        ISourceNavigator viewNav,
        IFhirSchemaProvider schemaProvider,
        SqlOnFhirEvaluator evaluator,
        IReadOnlyDictionary<string, string> vars,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var line in File.ReadLinesAsync(inputPath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var node = JsonSourceNodeFactory.Parse(line);
            if (node is null) continue;
            var element = node.ToElement(schemaProvider);
            var rows = evaluator.Evaluate(viewNav, element, vars);
            if (rows is null) continue;
            foreach (var row in rows) yield return row;
        }
    }

    private static ISourceNavigator? ParseViewDefinition(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSourceNodeFactory.Parse(json)?.ToSourceNavigator();
    }

    private static string? DetectFormat(string outputPath) =>
        Path.GetExtension(outputPath).ToUpperInvariant() switch
        {
            ".PARQUET" => "parquet",
            ".CSV"     => "csv",
            ".NDJSON"  => "ndjson",
            _          => null
        };

    private static void EnsureParentDirectory(string outputPath)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);
    }

    private static void Print(bool quiet, string message) { if (!quiet) Console.WriteLine(message); }

    private static (ParquetSchema Schema, Dictionary<string, string> TypeMap) BuildParquetSchema(
        IReadOnlyList<ColumnSchema> columnSchemas)
    {
        var fields  = new List<DataField>();
        var typeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columnSchemas)
        {
            var sqlType = (col.Type ?? "STRING").ToUpperInvariant();
            fields.Add(MapToParquetField(col.Name, sqlType));
            typeMap[col.Name] = sqlType;
        }
        if (fields.Count == 0) { fields.Add(new DataField<string>("id")); typeMap["id"] = "STRING"; }
        return (new ParquetSchema(fields), typeMap);
    }

    private static DataField MapToParquetField(string name, string sqlType) => sqlType switch
    {
        "BOOLEAN"  => new DataField<bool?>(name),
        "INTEGER"  => new DataField<int?>(name),
        "DECIMAL"  => new DataField<decimal?>(name),
        "DATE"     => new DataField<DateTime?>(name),
        "DATETIME" => new DataField<DateTimeOffset?>(name),
        _          => new DataField<string>(name)
    };
}
