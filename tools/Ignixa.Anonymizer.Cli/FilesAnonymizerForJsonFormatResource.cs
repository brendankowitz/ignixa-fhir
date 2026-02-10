// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.Anonymizer.PartitionedExecution;

namespace Ignixa.Anonymizer.Cli;

public class FilesAnonymizerForJsonFormatResource
{
    private readonly string _inputFolder;
    private readonly string _outputFolder;
    private readonly string _configFilePath;
    private readonly AnonymizationToolOptions _options;
    private readonly ISchema _schema;

    public FilesAnonymizerForJsonFormatResource(
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
        var resourceFileList = Directory.EnumerateFiles(_inputFolder, "*.json", directorySearchOption).ToList();
        Console.WriteLine($"Find {resourceFileList.Count} json resource files in '{_inputFolder}'.");

        FhirEnumerableReader<string> reader = new FhirEnumerableReader<string>(resourceFileList);
        FhirPartitionedExecutor<string, string> executor = new FhirPartitionedExecutor<string, string>(reader, null)
        {
            KeepOrder = false,
            BatchSize = 1,
            PartitionCount = Environment.ProcessorCount * 2
        };

        executor.AnonymizerFunctionAsync = async file =>
        {
            try
            {
                return await FileAnonymize(file).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ErrorMessage: {ex}");
                throw;
            }
        };

        Stopwatch stopWatch = new Stopwatch();
        stopWatch.Start();

        int completedCount = 0;
        int skippedCount = 0;
        Progress<BatchAnonymizeProgressDetail> progress = new Progress<BatchAnonymizeProgressDetail>();
        progress.ProgressChanged += (obj, args) =>
        {
            Interlocked.Add(ref completedCount, args.ProcessCompleted);
            Interlocked.Add(ref skippedCount, args.ProcessSkipped);
            Console.WriteLine($"[{stopWatch.Elapsed}][tid:{args.CurrentThreadId}]: {completedCount} Process completed. {skippedCount} Process skipped.");
        };

        await executor.ExecuteAsync(cancellationToken: CancellationToken.None, progress).ConfigureAwait(false);
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
            var settings = new AnonymizerConfigurations.AnonymizerSettings
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
