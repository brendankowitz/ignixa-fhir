// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Diagnostics;
using System.Threading.Channels;
using Ignixa.Abstractions;

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

            using (FileStream inputStream = new FileStream(bulkResourceFileName, FileMode.Open))
            using (FileStream outputStream = new FileStream(tempBulkResourceOutputFileName, FileMode.Create))
            {
                var engine = AnonymizerEngine.CreateWithFileContext(_configFilePath, _schema, bulkResourceFileName, _inputFolder);
                var settings = new AnonymizerConfigurations.AnonymizerSettings
                {
                    IsPrettyOutput = false,
                    ValidateInput = _options.ValidateInput,
                    ValidateOutput = _options.ValidateOutput
                };

                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                using var cts = new CancellationTokenSource();
                var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1000)
                {
                    FullMode = BoundedChannelFullMode.Wait
                });

                int completedCount = 0;
                int skippedCount = 0;

                // Producer: read lines from input stream
                var producerTask = Task.Run(async () =>
                {
                    using var reader = new StreamReader(inputStream);
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        await channel.Writer.WriteAsync(line, cts.Token);
                    }
                    channel.Writer.Complete();
                }, cts.Token);

                // Consumer: anonymize and write to output stream
                var consumerTask = Task.Run(async () =>
                {
                    using var writer = new StreamWriter(outputStream);

                    await foreach (var line in channel.Reader.ReadAllAsync(cts.Token))
                    {
                        try
                        {
                            var anonymized = engine.AnonymizeJson(line, settings);
                            if (!string.IsNullOrEmpty(anonymized))
                            {
                                await writer.WriteLineAsync(anonymized);
                                Interlocked.Increment(ref completedCount);
                            }
                            else
                            {
                                Interlocked.Increment(ref skippedCount);
                            }

                            if ((completedCount + skippedCount) % 100 == 0)
                            {
                                Console.WriteLine($"[{stopWatch.Elapsed}]: {completedCount} completed, {skippedCount} skipped");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"ErrorMessage: {ex}");
                            throw;
                        }
                    }

                    await writer.FlushAsync();
                }, cts.Token);

                await Task.WhenAll(producerTask, consumerTask).ConfigureAwait(false);
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
