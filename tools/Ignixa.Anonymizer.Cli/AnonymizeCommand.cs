// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.CommandLine;
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Configuration;
using Ignixa.Anonymizer.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ignixa.Anonymizer.Cli;

public static class AnonymizeCommand
{
    public static Command Create(IFhirSchemaProvider schema)
    {
        var command = new Command("anonymize", "Anonymize FHIR resources in a folder");

        var inputOption = new Option<string?>("--input") { Description = "Input folder containing FHIR resource files" };
        var outputOption = new Option<string?>("--output") { Description = "Output folder for anonymized resource files" };
        var configOption = new Option<string>("--config") { Description = "Path to anonymizer configuration file", DefaultValueFactory = _ => "configuration-sample.json" };
        var bulkDataOption = new Option<bool>("--bulk-data") { Description = "Process files in NDJSON bulk data format", DefaultValueFactory = _ => false };
        var skipOption = new Option<bool>("--skip-existing") { Description = "Skip files that already exist in the output folder", DefaultValueFactory = _ => false };
        var recursiveOption = new Option<bool>("--recursive") { Description = "Process resource files recursively", DefaultValueFactory = _ => false };
        var verboseOption = new Option<bool>("--verbose") { Description = "Enable verbose logging", DefaultValueFactory = _ => false };
        var validateInputOption = new Option<bool>("--validate-input") { Description = "Validate input resources", DefaultValueFactory = _ => false };
        var validateOutputOption = new Option<bool>("--validate-output") { Description = "Validate anonymized output resources", DefaultValueFactory = _ => false };

        command.Options.Add(inputOption);
        command.Options.Add(outputOption);
        command.Options.Add(configOption);
        command.Options.Add(bulkDataOption);
        command.Options.Add(skipOption);
        command.Options.Add(recursiveOption);
        command.Options.Add(verboseOption);
        command.Options.Add(validateInputOption);
        command.Options.Add(validateOutputOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var input = parseResult.GetValue(inputOption);
            var output = parseResult.GetValue(outputOption);
            var config = parseResult.GetValue(configOption)!;
            var bulkData = parseResult.GetValue(bulkDataOption);
            var skip = parseResult.GetValue(skipOption);
            var recursive = parseResult.GetValue(recursiveOption);
            var verbose = parseResult.GetValue(verboseOption);
            var validateInput = parseResult.GetValue(validateInputOption);
            var validateOutput = parseResult.GetValue(validateOutputOption);

            await RunAnonymization(schema, input, output, config, bulkData, skip, recursive, verbose, validateInput, validateOutput, cancellationToken);
        });

        return command;
    }

    private static async Task RunAnonymization(
        IFhirSchemaProvider schema,
        string? inputFolder,
        string? outputFolder,
        string configFilePath,
        bool bulkData,
        bool skipExisting,
        bool recursive,
        bool verbose,
        bool validateInput,
        bool validateOutput,
        CancellationToken cancellationToken)
    {
        ServiceProvider? serviceProvider = null;
        try
        {
            if (string.IsNullOrEmpty(inputFolder))
            {
                Console.Error.WriteLine("Error: --input is required. Please specify the input folder.");
                Environment.ExitCode = 1;
                return;
            }

            if (string.IsNullOrEmpty(outputFolder))
            {
                Console.Error.WriteLine("Error: --output is required. Please specify the output folder.");
                Environment.ExitCode = 1;
                return;
            }

            var inputPath = Path.GetFullPath(inputFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var outputPath = Path.GetFullPath(outputFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Error: Input and output folders are the same. Please choose another folder.");
                Environment.ExitCode = 1;
                return;
            }

            var configFullPath = Path.GetFullPath(configFilePath);
            var configResult = AnonymizerOptionsLoader.LoadFromFile(configFullPath);
            if (!configResult.IsSuccess)
            {
                Console.Error.WriteLine($"Error: Failed to load config: {configResult.Error.Message}");
                Environment.ExitCode = 1;
                return;
            }

            Directory.CreateDirectory(outputFolder);

            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.AddFilter("Microsoft", LogLevel.Warning)
                       .AddFilter("System", LogLevel.Warning)
                       .AddFilter("Ignixa.Anonymizer", verbose ? LogLevel.Trace : LogLevel.Information)
                       .AddConsole();
            });

            services.AddFhirAnonymizer(builder =>
            {
                builder.WithConfigurationFile(configFullPath);
            });

            services.AddSingleton(schema);

            serviceProvider = services.BuildServiceProvider();

            var engine = serviceProvider.GetRequiredService<IAnonymizerEngine>();

            var toolOptions = new AnonymizationToolOptions
            {
                IsRecursive = recursive,
                SkipExistedFile = skipExisting,
                ValidateInput = validateInput,
                ValidateOutput = validateOutput
            };

            if (bulkData)
            {
                await new FilesAnonymizerForNdJsonFormatResource(engine, inputFolder, outputFolder, toolOptions)
                    .AnonymizeAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await new FilesAnonymizerForJsonFormatResource(engine, inputFolder, outputFolder, toolOptions)
                    .AnonymizeAsync(cancellationToken).ConfigureAwait(false);
            }

            Console.WriteLine($"Finished processing '{inputFolder}'.");
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation was cancelled.");
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
        finally
        {
            if (serviceProvider is not null)
            {
                await serviceProvider.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
