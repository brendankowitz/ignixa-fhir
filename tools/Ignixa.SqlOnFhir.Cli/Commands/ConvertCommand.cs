// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.SqlOnFhir.Cli.Helpers;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Writers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.SqlOnFhir.Cli.Commands;

/// <summary>
/// Command for converting FHIR resources to Parquet or CSV format using a ViewDefinition.
/// </summary>
internal static class ConvertCommand
{
    public static Command Create()
    {
        var convertCommand = new Command("convert", "Convert FHIR resources using a ViewDefinition");

        var viewDefinitionOption = new Option<string>("--viewdefinition", "Path to ViewDefinition JSON file") { IsRequired = true };
        var inputOption = new Option<string>("--input", "Path to input NDJSON file containing FHIR resources") { IsRequired = true };
        var outputOption = new Option<string>("--out", "Path to output file") { IsRequired = true };
        var formatOption = new Option<string>("--format", () => "parquet", "Output format: parquet or csv");

        convertCommand.AddOption(viewDefinitionOption);
        convertCommand.AddOption(inputOption);
        convertCommand.AddOption(outputOption);
        convertCommand.AddOption(formatOption);

        convertCommand.SetHandler(async (viewDefinitionPath, inputPath, outputPath, format) =>
        {
            await HandleConvertCommand(viewDefinitionPath, inputPath, outputPath, format);
        }, viewDefinitionOption, inputOption, outputOption, formatOption);

        return convertCommand;
    }

    private static async Task HandleConvertCommand(
        string viewDefinitionPath,
        string inputPath,
        string outputPath,
        string format)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

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

            // Extract schema
            var (schema, columnTypeMap) = SchemaExtractor.ExtractParquetSchema(viewDefNavigator);
            Console.WriteLine($"✓ Extracted schema with {schema.Fields.Count} columns");

            // Detect FHIR version from first resource
            var schemaProvider = await FhirVersionDetector.DetectFhirVersionAsync(inputPath);
            if (schemaProvider == null)
            {
                Console.WriteLine("✗ Could not detect FHIR version from input file");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"✓ Detected FHIR version: {schemaProvider.GetType().Name}");

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
}
