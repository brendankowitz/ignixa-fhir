// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Configuration;
using Ignixa.Anonymizer.Models;
using Xunit;

namespace Ignixa.Anonymizer.FunctionalTests;

public static class FunctionalTestUtility
{
    public static async Task VerifySingleJsonResourceFromFileAsync(
        IAnonymizerEngine engine,
        string testFile,
        string targetFile,
        RequestOptions? settings = null)
    {
        Console.WriteLine($"VerifySingleJsonResourceFromFileAsync. TestFile: {testFile}, TargetFile: {targetFile}");
        string testContent = await File.ReadAllTextAsync(testFile);

        var result = await engine.AnonymizeAsync(testContent, settings);

        result.IsSuccess.ShouldBeTrue($"Anonymization failed: {(result.IsSuccess ? "" : result.Error.Message)}");

        string standardizedResult = Standardize(result.Value.AnonymizedJson);

        var updateTargets = Environment.GetEnvironmentVariable("UPDATE_TARGETS");
        if (!string.IsNullOrEmpty(updateTargets) && updateTargets == "1")
        {
            var newFile = targetFile + ".new";
            await File.WriteAllTextAsync(newFile, standardizedResult);
            Console.WriteLine($"Generated new target file: {newFile}");
            Console.WriteLine($"To apply: copy /Y \"{newFile}\" \"{targetFile}\"");
            return;
        }

        string targetContent = await File.ReadAllTextAsync(targetFile);
        Assert.Equal(Standardize(targetContent), standardizedResult);
    }

    public static async Task<Result<AnonymizationResult>> AnonymizeFromFileAsync(
        IAnonymizerEngine engine,
        string testFile,
        RequestOptions? settings = null)
    {
        string testContent = await File.ReadAllTextAsync(testFile);
        return await engine.AnonymizeAsync(testContent, settings);
    }

    public static async Task<string> GetActualOutputAsync(
        IAnonymizerEngine engine,
        string testFile,
        RequestOptions? settings = null)
    {
        string testContent = await File.ReadAllTextAsync(testFile);
        var result = await engine.AnonymizeAsync(testContent, settings);
        result.IsSuccess.ShouldBeTrue($"Anonymization failed: {(result.IsSuccess ? "" : result.Error.Message)}");
        return Standardize(result.Value.AnonymizedJson);
    }

    private static string Standardize(string jsonContent)
    {
        var node = JsonNode.Parse(jsonContent);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        return node?.ToJsonString(options) ?? string.Empty;
    }
}
