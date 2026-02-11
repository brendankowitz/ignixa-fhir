// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Diagnostics;
using Ignixa.Abstractions;

namespace Ignixa.Anonymizer.Cli;

public class FilesAnonymizerForJsonFormatResource
{
    private readonly string _inputFolder;
    private readonly string _outputFolder;
    private readonly string _configFilePath;
    private readonly AnonymizationToolOptions _options;
    private readonly IFhirSchemaProvider _schema;

    public FilesAnonymizerForJsonFormatResource(
        string configFilePath,
        string inputFolder,
        string outputFolder,
        AnonymizationToolOptions options,
        IFhirSchemaProvider schema)
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
        var resourceFileList = Directory.EnumerateFiles(_inputFolder, "*.json", directorySearchOption).ToList();
        Console.WriteLine($"Find {resourceFileList.Count} json resource files in '{_inputFolder}'.");

        Stopwatch stopWatch = new Stopwatch();
        stopWatch.Start();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount * 2,
            CancellationToken = CancellationToken.None
        };

        int completedCount = 0;
        int skippedCount = 0;

        await Parallel.ForEachAsync(
            resourceFileList,
            options,
            async (file, ct) =>
            {
                try
                {
                    var result = await FileAnonymize(file).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(result))
                    {
                        Interlocked.Increment(ref skippedCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref completedCount);
                    }

                    if ((completedCount + skippedCount) % 10 == 0)
                    {
                        Console.WriteLine($"[{stopWatch.Elapsed}]: {completedCount} completed, {skippedCount} skipped");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error processing {file}: {ex.Message}");
                    throw;
                }
            }).ConfigureAwait(false);

        Console.WriteLine($"Finished: {completedCount} completed, {skippedCount} skipped in {stopWatch.Elapsed}");
    }

    public async Task<string> FileAnonymize(string fileName)
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
            return string.Empty;
        }

        string resourceJson = await File.ReadAllTextAsync(fileName).ConfigureAwait(false);
        try
        {
            var engine = AnonymizerEngine.CreateWithFileContext(_configFilePath, _schema, fileName, _inputFolder);
            var settings = new Configuration.AnonymizerSettings
            {
                IsPrettyOutput = true,
                ValidateInput = _options.ValidateInput,
                ValidateOutput = _options.ValidateOutput
            };
            var resourceResult = engine.AnonymizeJson(resourceJson, settings);
            await File.WriteAllTextAsync(resourceOutputFileName, resourceResult).ConfigureAwait(false);
            return resourceResult;
        }
        catch (Exception innerException)
        {
            Console.Error.WriteLine($"[{fileName}] Error.\nErrorMessage: {innerException}");
            throw;
        }
    }

    private static string GetResourceOutputFileName(string fileName, string inputFolder, string outputFolder)
    {
        var partialFilename = fileName[inputFolder.Length..]
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(outputFolder, partialFilename);
    }
}
