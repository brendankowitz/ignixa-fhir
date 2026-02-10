// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.Anonymizer.PartitionedExecution;

namespace Ignixa.Anonymizer.Cli;

public class FilesAnonymizerForNdJsonFormatResource
{
    private readonly string _inputFolder;
    private readonly string _outputFolder;
    private readonly string _configFilePath;
    private readonly AnonymizationToolOptions _options;
    private readonly ISchema _schema;

    public FilesAnonymizerForNdJsonFormatResource(
        string configFilePath,
        string inputFolder,
        string outputFolder,
        AnonymizationToolOptions options,
        ISchema schema)
    {
        _inputFolder = inputFolder;
        _outputFolder = outputFolder;
        _configFilePath = configFilePath;
        _options = options;
        _schema = schema;
    }

    public async Task AnonymizeAsync()
    {
        var directorySearchOption = _options.IsRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var bulkResourceFileList = Directory.EnumerateFiles(_inputFolder, "*.ndjson", directorySearchOption).ToList();
        Console.WriteLine($"Find {bulkResourceFileList.Count} bulk data resource files in '{_inputFolder}'.");

        foreach (var bulkResourceFileName in bulkResourceFileList)
        {
            Console.WriteLine($"Processing {bulkResourceFileName}");

            var bulkResourceOutputFileName = GetResourceOutputFileName(bulkResourceFileName, _inputFolder, _outputFolder);
            var tempBulkResourceOutputFileName = GetTempFileName(bulkResourceOutputFileName);
            if (_options.IsRecursive)
            {
                var resourceOutputFolder = Path.GetDirectoryName(bulkResourceOutputFileName);
                Directory.CreateDirectory(resourceOutputFolder!);
            }

            if (_options.SkipExistedFile && File.Exists(bulkResourceOutputFileName))
            {
                Console.WriteLine($"Skip processing on file {bulkResourceOutputFileName} since it already exists in destination.");
                continue;
            }

            if (File.Exists(bulkResourceOutputFileName))
            {
                Console.WriteLine($"Remove existed target file {bulkResourceOutputFileName}.");
                File.Delete(bulkResourceOutputFileName);
            }

            int completedCount = 0;
            int skippedCount = 0;
            int consumeCompletedCount = 0;
            using (FileStream inputStream = new FileStream(bulkResourceFileName, FileMode.Open))
            using (FileStream outputStream = new FileStream(tempBulkResourceOutputFileName, FileMode.Create))
            {
                using FhirStreamReader reader = new FhirStreamReader(inputStream);
                using FhirStreamConsumer consumer = new FhirStreamConsumer(outputStream);
                var engine = AnonymizerEngine.CreateWithFileContext(_configFilePath, _schema, bulkResourceFileName, _inputFolder);
                Func<string, string> anonymizeFunction = (content) =>
                {
                    try
                    {
                        var settings = new AnonymizerConfigurations.AnonymizerSettings
                        {
                            IsPrettyOutput = false,
                            ValidateInput = _options.ValidateInput,
                            ValidateOutput = _options.ValidateOutput
                        };
                        return engine.AnonymizeJson(content, settings);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"ErrorMessage: {ex}");
                        throw;
                    }
                };

                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                FhirPartitionedExecutor<string, string> executor = new FhirPartitionedExecutor<string, string>(reader, consumer, anonymizeFunction)
                {
                    PartitionCount = Environment.ProcessorCount * 2
                };

                Progress<BatchAnonymizeProgressDetail> progress = new Progress<BatchAnonymizeProgressDetail>();
                progress.ProgressChanged += (obj, args) =>
                {
                    Interlocked.Add(ref completedCount, args.ProcessCompleted);
                    Interlocked.Add(ref skippedCount, args.ProcessSkipped);
                    Interlocked.Add(ref consumeCompletedCount, args.ConsumeCompleted);
                    Console.WriteLine($"[{stopWatch.Elapsed}][tid:{args.CurrentThreadId}]: {completedCount} Process completed. {skippedCount} Process skipped. {consumeCompletedCount} Consume completed.");
                };

                await executor.ExecuteAsync(CancellationToken.None, progress).ConfigureAwait(false);
            }

            File.Move(tempBulkResourceOutputFileName, bulkResourceOutputFileName);
            Console.WriteLine($"Finished processing '{bulkResourceFileName}'!");
        }
    }

    private static string GetTempFileName(string pathFileName)
    {
        string directory = Path.GetDirectoryName(pathFileName)!;
        return Path.Combine(directory, $"{Guid.NewGuid():N}");
    }

    private static string GetResourceOutputFileName(string fileName, string inputFolder, string outputFolder)
    {
        var partialFilename = fileName[inputFolder.Length..]
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(outputFolder, partialFilename);
    }
}
