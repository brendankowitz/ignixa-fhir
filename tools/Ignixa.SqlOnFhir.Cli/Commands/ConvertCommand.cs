// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Parsing;
using Ignixa.SqlOnFhir.Writers;
using Microsoft.Extensions.Logging.Abstractions;
using Parquet.Data;
using Parquet.Schema;

namespace Ignixa.SqlOnFhir.Cli.Commands;

/// <summary>
/// Command for converting FHIR resources to Parquet or CSV format using a ViewDefinition.
/// </summary>
internal static class ConvertCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var convertCommand = new Command("convert", "Convert FHIR resources using a ViewDefinition");

        var viewDefinitionOption = new Option<string>("--viewdefinition", "Path to ViewDefinition JSON file") { IsRequired = true };
        var inputOption = new Option<string>("--input", "Path to input NDJSON file containing FHIR resources") { IsRequired = true };
        var outputOption = new Option<string>("--out", "Path to output file (extension determines format: .parquet or .csv)") { IsRequired = true };

        convertCommand.AddOption(viewDefinitionOption);
        convertCommand.AddOption(inputOption);
        convertCommand.AddOption(outputOption);

        convertCommand.SetHandler(async (viewDefinitionPath, inputPath, outputPath) =>
        {
            await HandleConvertCommand(schemaProvider, fhirVersion, viewDefinitionPath, inputPath, outputPath);
        }, viewDefinitionOption, inputOption, outputOption);

        return convertCommand;
    }

    private static async Task HandleConvertCommand(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string viewDefinitionPath,
        string inputPath,
        string outputPath)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Detect format from file extension
            var format = DetectFormatFromExtension(outputPath);
            if (format == null)
            {
                Console.WriteLine($"✗ Unsupported output file extension. Use .parquet or .csv");
                Environment.ExitCode = 1;
                return;
            }

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
            
            // Build Parquet schema and column type map from extracted schema
            var (schema, columnTypeMap) = BuildParquetSchema(columnSchemas);
            Console.WriteLine($"✓ Extracted schema with {schema.Fields.Count} columns");

            // Use the provided schema provider from the command-line argument
            Console.WriteLine($"✓ Using FHIR version: {fhirVersion.ToUpperInvariant()}");

            // Create evaluator
            var evaluator = new SqlOnFhirEvaluator();

            // Process resources and write output
            var resourcesProcessed = 0;
            var rowsGenerated = 0;
            var logger = NullLogger.Instance;

            if (format.Equals("parquet", StringComparison.OrdinalIgnoreCase))
            {
                await using var writer = new ParquetFileWriter(outputPath, schema, logger, columnTypeMap);

                await foreach (var row in ProcessResourcesAsync(inputPath, viewDefNavigator, schemaProvider, evaluator))
                {
                    await writer.WriteRowAsync(row);
                    rowsGenerated++;
                }

                await writer.FlushAsync();
                resourcesProcessed = await CountResourcesAsync(inputPath);

                Console.WriteLine($"✓ Converted {resourcesProcessed} resources to {rowsGenerated} rows");
                Console.WriteLine($"✓ Wrote Parquet file: {outputPath} ({writer.BytesWritten:N0} bytes)");
            }
            else if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            {
                await using var writer = new CsvFileWriter(outputPath, logger);

                await foreach (var row in ProcessResourcesAsync(inputPath, viewDefNavigator, schemaProvider, evaluator))
                {
                    await writer.WriteRowAsync(row);
                    rowsGenerated++;
                }

                await writer.FlushAsync();
                resourcesProcessed = await CountResourcesAsync(inputPath);

                Console.WriteLine($"✓ Converted {resourcesProcessed} resources to {rowsGenerated} rows");
                Console.WriteLine($"✓ Wrote CSV file: {outputPath} ({writer.BytesWritten:N0} bytes)");
            }
            else
            {
                Console.WriteLine($"✗ Unknown format: {format}. Supported formats: parquet, csv");
                Environment.ExitCode = 1;
                return;
            }

            stopwatch.Stop();
            Console.WriteLine($"✓ Completed in {stopwatch.Elapsed.TotalSeconds:F1} seconds");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.ExitCode = 1;
        }
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

    private static async Task<int> CountResourcesAsync(string inputPath)
    {
        var count = 0;
        await foreach (var line in File.ReadLinesAsync(inputPath))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                count++;
            }
        }
        return count;
    }

    private static string? DetectFormatFromExtension(string outputPath)
    {
        var extension = Path.GetExtension(outputPath).ToUpperInvariant();
        return extension switch
        {
            ".PARQUET" => "parquet",
            ".CSV" => "csv",
            _ => null
        };
    }

    private static (ParquetSchema Schema, Dictionary<string, string> ColumnTypeMap) BuildParquetSchema(
        IReadOnlyList<ColumnSchema> columnSchemas)
    {
        var fields = new List<DataField>();
        var columnTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columnSchemas)
        {
            var columnName = column.Name;
            var sqlType = (column.Type ?? "STRING").ToUpperInvariant();

            // Map SQL type to Parquet DataField
            var field = MapSqlTypeToParquetField(columnName, sqlType);
            fields.Add(field);

            // Store type mapping for later use
            columnTypeMap[columnName] = sqlType;
        }

        // If no columns defined, create a minimal schema
        if (fields.Count == 0)
        {
            fields.Add(new DataField<string>("id"));
            columnTypeMap["id"] = "STRING";
        }

        var schema = new ParquetSchema(fields);
        return (schema, columnTypeMap);
    }

    private static DataField MapSqlTypeToParquetField(string columnName, string sqlType)
    {
        return sqlType switch
        {
            "STRING" => new DataField<string>(columnName),
            "BOOLEAN" => new DataField<bool?>(columnName),
            "INTEGER" => new DataField<int?>(columnName),
            "DECIMAL" => new DataField<decimal?>(columnName),
            "DATE" => new DataField<DateTime?>(columnName),
            "DATETIME" => new DataField<DateTimeOffset?>(columnName),
            _ => new DataField<string>(columnName) // Default to string for unknown types
        };
    }
}
