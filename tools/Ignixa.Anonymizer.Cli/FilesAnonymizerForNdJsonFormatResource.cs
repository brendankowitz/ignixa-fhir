// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Anonymizer.Cli;

public class FilesAnonymizerForNdJsonFormatResource(
    IAnonymizerEngine engine,
    string inputFolder,
    string outputFolder,
    AnonymizationToolOptions options)
{
    private readonly string _inputFolder = inputFolder;
    private readonly string _outputFolder = outputFolder;
    private readonly AnonymizationToolOptions _options = options;
    private readonly IAnonymizerEngine _engine = engine;

    public async Task AnonymizeAsync(CancellationToken cancellationToken = default)
    {
        var directorySearchOption = _options.IsRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var bulkResourceFileList = Directory.EnumerateFiles(_inputFolder, "*.ndjson", directorySearchOption).ToList();
        Console.WriteLine($"Find {bulkResourceFileList.Count} bulk data resource files in '{_inputFolder}'.");

        foreach (var bulkResourceFileName in bulkResourceFileList)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

            await ProcessNdJsonFile(bulkResourceFileName, tempBulkResourceOutputFileName, cancellationToken).ConfigureAwait(false);

            File.Move(tempBulkResourceOutputFileName, bulkResourceOutputFileName);
            Console.WriteLine($"Finished processing '{bulkResourceFileName}'!");
        }
    }

    private async Task ProcessNdJsonFile(string inputFile, string outputFile, CancellationToken cancellationToken)
    {
        var settings = new RequestOptions
        {
            IsPrettyOutput = false,
            ValidateInput = _options.ValidateInput,
            ValidateOutput = _options.ValidateOutput
        };

        var stopWatch = Stopwatch.StartNew();

        await using var outputWriter = new StreamWriter(outputFile);

        int completedCount = 0;
        int errorCount = 0;

        var lines = ReadLinesAsync(inputFile, cancellationToken);

        await foreach (var result in _engine.AnonymizeManyAsync(lines, settings, cancellationToken).ConfigureAwait(false))
        {
            if (result.IsSuccess)
            {
                await outputWriter.WriteLineAsync(result.Value.AnonymizedJson).ConfigureAwait(false);
                completedCount++;
            }
            else
            {
                Console.Error.WriteLine($"Error: {result.Error.Message}");
                errorCount++;
            }

            var total = completedCount + errorCount;
            if (total % 100 == 0)
            {
                Console.WriteLine($"[{stopWatch.Elapsed}]: {completedCount} completed, {errorCount} errors");
            }
        }

        await outputWriter.FlushAsync(cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"File complete: {completedCount} succeeded, {errorCount} failed in {stopWatch.Elapsed}");
    }

    private static async IAsyncEnumerable<ResourceJsonNode> ReadLinesAsync(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(filePath);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            yield return ResourceJsonNode.Parse(line);
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
