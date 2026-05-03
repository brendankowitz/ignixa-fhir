// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Diagnostics;
using Ignixa.Abstractions;

namespace Ignixa.DeId.Cli;

public class FilesDeIdForJsonFormatResource(
    IDeIdEngine engine,
    string inputFolder,
    string outputFolder,
    DeIdToolOptions options)
{
    private readonly string _inputFolder = inputFolder;
    private readonly string _outputFolder = outputFolder;
    private readonly DeIdToolOptions _options = options;
    private readonly IDeIdEngine _engine = engine;

    public async Task DeidentifyAsync(CancellationToken cancellationToken = default)
    {
        var directorySearchOption = _options.IsRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var resourceFileList = Directory.EnumerateFiles(_inputFolder, "*.json", directorySearchOption).ToList();
        Console.WriteLine($"Find {resourceFileList.Count} json resource files in '{_inputFolder}'.");

        var stopWatch = Stopwatch.StartNew();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount * 2,
            CancellationToken = cancellationToken
        };

        int completedCount = 0;
        int skippedCount = 0;
        int errorCount = 0;

        await Parallel.ForEachAsync(
            resourceFileList,
            parallelOptions,
            async (file, ct) =>
            {
                try
                {
                    var status = await FileDeidentify(file, ct).ConfigureAwait(false);
                    switch (status)
                    {
                        case FileStatus.Completed:
                            Interlocked.Increment(ref completedCount);
                            break;
                        case FileStatus.Skipped:
                            Interlocked.Increment(ref skippedCount);
                            break;
                        case FileStatus.Error:
                            Interlocked.Increment(ref errorCount);
                            break;
                    }

                    var total = completedCount + skippedCount + errorCount;
                    if (total % 10 == 0)
                    {
                        Console.WriteLine($"[{stopWatch.Elapsed}]: {completedCount} completed, {skippedCount} skipped, {errorCount} errors");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error processing {file}: {ex.Message}");
                    Interlocked.Increment(ref errorCount);
                }
            }).ConfigureAwait(false);

        Console.WriteLine($"Finished: {completedCount} completed, {skippedCount} skipped, {errorCount} errors in {stopWatch.Elapsed}");
    }

    private async Task<FileStatus> FileDeidentify(string fileName, CancellationToken cancellationToken)
    {
        var resourceOutputFileName = GetResourceOutputFileName(fileName, _inputFolder, _outputFolder);
        if (_options.IsRecursive)
        {
            var resourceOutputFolder = Path.GetDirectoryName(resourceOutputFileName);
            Directory.CreateDirectory(resourceOutputFolder!);
        }

        if (_options.SkipExistedFile && File.Exists(resourceOutputFileName))
        {
            Console.WriteLine($"Skip processing on file {fileName} since it already exists in destination.");
            return FileStatus.Skipped;
        }

        string resourceJson = await File.ReadAllTextAsync(fileName, cancellationToken).ConfigureAwait(false);

        var settings = new RequestOptions
        {
            IsPrettyOutput = true,
            ValidateInput = _options.ValidateInput,
            ValidateOutput = _options.ValidateOutput
        };

        var result = await _engine.DeidentifyAsync(resourceJson, settings, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await File.WriteAllTextAsync(resourceOutputFileName, result.Value.DeidentifiedJson, cancellationToken).ConfigureAwait(false);
            return FileStatus.Completed;
        }
        else
        {
            Console.Error.WriteLine($"[{fileName}] Error: {result.Error.Message}");
            return FileStatus.Error;
        }
    }

    private static string GetResourceOutputFileName(string fileName, string inputFolder, string outputFolder)
    {
        var partialFilename = fileName[inputFolder.Length..]
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(outputFolder, partialFilename);
    }

    private enum FileStatus
    {
        Completed,
        Skipped,
        Error
    }
}
