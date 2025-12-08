// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Parsing;

namespace Ignixa.SqlOnFhir.Cli.Commands;

/// <summary>
/// Command for previewing the schema and sample rows from a ViewDefinition.
/// </summary>
internal static class PreviewCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var previewCommand = new Command("preview", "Preview schema and sample rows from a ViewDefinition");

        var viewDefinitionOption = new Option<string>("--viewdefinition", "Path to ViewDefinition JSON file") { IsRequired = true };
        var inputOption = new Option<string>("--input", "Path to input NDJSON file containing FHIR resources") { IsRequired = true };
        var rowsOption = new Option<int>("--rows", () => 5, "Number of sample rows to display");

        previewCommand.AddOption(viewDefinitionOption);
        previewCommand.AddOption(inputOption);
        previewCommand.AddOption(rowsOption);

        previewCommand.SetHandler(async (viewDefinitionPath, inputPath, rows) =>
        {
            await HandlePreviewCommand(schemaProvider, fhirVersion, viewDefinitionPath, inputPath, rows);
        }, viewDefinitionOption, inputOption, rowsOption);

        return previewCommand;
    }

    private static async Task HandlePreviewCommand(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string viewDefinitionPath,
        string inputPath,
        int maxRows)
    {
        try
        {
            // Validate input files exist
            if (!File.Exists(viewDefinitionPath))
            {
                Console.WriteLine($"✗ ViewDefinition file not found: {viewDefinitionPath}");
                Environment.ExitCode = 1;
                return;
            }

            if (!File.Exists(inputPath))
            {
                Console.WriteLine($"✗ Input file not found: {inputPath}");
                Environment.ExitCode = 1;
                return;
            }

            // Read and parse ViewDefinition
            var viewDefJson = await File.ReadAllTextAsync(viewDefinitionPath);
            var viewDefNode = JsonSourceNodeFactory.Parse(viewDefJson);
            if (viewDefNode == null)
            {
                Console.WriteLine($"✗ Failed to parse ViewDefinition: {viewDefinitionPath}");
                Environment.ExitCode = 1;
                return;
            }

            var viewDefNavigator = viewDefNode.ToSourceNavigator();

            // Parse ViewDefinition expression
            var viewDefExpression = ViewDefinitionExpressionParser.Parse(viewDefNavigator);

            // Extract schema using SqlOnFhirSchemaEvaluator
            var schemaEvaluator = new SqlOnFhirSchemaEvaluator();
            var columnSchemas = schemaEvaluator.GetSchema(viewDefExpression);
            
            Console.WriteLine();
            Console.WriteLine("=== Schema ===");
            Console.WriteLine();
            
            if (columnSchemas.Count > 0)
            {
                var maxColumnNameLength = columnSchemas.Max(c => c.Name.Length);
                foreach (var column in columnSchemas.OrderBy(c => c.Name))
                {
                    var typeStr = column.Type ?? "inferred";
                    var collectionStr = column.Collection ? " (collection)" : "";
                    Console.WriteLine($"  {column.Name.PadRight(maxColumnNameLength)}  {typeStr}{collectionStr}");
                }
            }

            // Use the provided schema provider from the command-line argument
            Console.WriteLine();
            Console.WriteLine($"Using FHIR version: {fhirVersion.ToUpperInvariant()}");

            // Create evaluator
            var evaluator = new SqlOnFhirEvaluator();

            // Process and display sample rows
            var sampleRows = new List<Dictionary<string, object?>>();
            await foreach (var row in ProcessResourcesAsync(inputPath, viewDefNavigator, schemaProvider, evaluator))
            {
                sampleRows.Add(row);
                if (sampleRows.Count >= maxRows)
                {
                    break;
                }
            }

            if (sampleRows.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine("✗ No rows generated from the input resources");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"=== Sample Rows ({sampleRows.Count}) ===");
            Console.WriteLine();

            // Display rows in a formatted table (using same order as schema output)
            var columns = columnSchemas.OrderBy(c => c.Name).Select(c => c.Name).ToList();
            DisplayTable(sampleRows, columns);

            Console.WriteLine();
            Console.WriteLine($"✓ Preview completed with {sampleRows.Count} sample rows");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.ExitCode = 1;
        }
    }

    private static void DisplayTable(List<Dictionary<string, object?>> rows, List<string> columns)
    {
        if (rows.Count == 0)
        {
            return;
        }

        // Calculate column widths
        var columnWidths = new Dictionary<string, int>();
        foreach (var column in columns)
        {
            var maxWidth = column.Length;
            foreach (var row in rows)
            {
                if (row.TryGetValue(column, out var value) && value != null)
                {
                    var valueStr = FormatValue(value);
                    maxWidth = Math.Max(maxWidth, valueStr.Length);
                }
            }
            // Limit column width to 50 characters for readability
            columnWidths[column] = Math.Min(maxWidth, 50);
        }

        // Print header
        var header = string.Join(" | ", columns.Select(c => c.PadRight(columnWidths[c])));
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length));

        // Print rows
        foreach (var row in rows)
        {
            var values = columns.Select(c =>
            {
                if (row.TryGetValue(c, out var value) && value != null)
                {
                    var valueStr = FormatValue(value);
                    if (valueStr.Length > columnWidths[c])
                    {
                        valueStr = string.Concat(valueStr.AsSpan(0, columnWidths[c] - 3), "...");
                    }
                    return valueStr.PadRight(columnWidths[c]);
                }
                return string.Empty.PadRight(columnWidths[c]);
            });
            Console.WriteLine(string.Join(" | ", values));
        }
    }

    private static string FormatValue(object value)
    {
        return value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            decimal d => d.ToString("0.##", CultureInfo.InvariantCulture),
            double dbl => dbl.ToString("0.##", CultureInfo.InvariantCulture),
            float f => f.ToString("0.##", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? string.Empty
        };
    }

    private static async IAsyncEnumerable<Dictionary<string, object?>> ProcessResourcesAsync(
        string inputPath,
        ISourceNavigator viewDefinition,
        IFhirSchemaProvider schemaProvider,
        SqlOnFhirEvaluator evaluator)
    {
        await foreach (var line in File.ReadLinesAsync(inputPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Parse resource
            var resourceNode = JsonSourceNodeFactory.Parse(line);
            if (resourceNode == null)
            {
                continue;
            }

            var resourceElement = resourceNode.ToElement(schemaProvider);

            // Evaluate ViewDefinition
            var rows = evaluator.Evaluate(viewDefinition, resourceElement);
            if (rows == null)
            {
                continue;
            }

            foreach (var row in rows)
            {
                yield return row;
            }
        }
    }
}
