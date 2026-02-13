// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.Anonymizer.Tests.Utilities;

public static class FunctionalTestUtility
{
    public static async Task VerifySingleJsonResourceFromFileAsync(
        IAnonymizerEngine engine,
        string testFile,
        string targetFile,
        RequestOptions? settings = null)
    {
        string testContent = await File.ReadAllTextAsync(testFile);

        var result = await engine.AnonymizeAsync(testContent, settings);

        result.IsSuccess.ShouldBeTrue($"Anonymization failed: {(result.IsSuccess ? "" : result.Error.Message)}");

        string standardizedResult = Standardize(result.Value.AnonymizedJson);

        var updateTargets = Environment.GetEnvironmentVariable("UPDATE_TARGETS");
        if (!string.IsNullOrEmpty(updateTargets) && updateTargets == "1")
        {
            var newFile = targetFile + ".new";
            await File.WriteAllTextAsync(newFile, standardizedResult);
            return;
        }

        string targetContent = await File.ReadAllTextAsync(targetFile);
        Standardize(targetContent).ShouldBe(standardizedResult);
    }

    private static string Standardize(string jsonContent)
    {
        var node = JsonNode.Parse(jsonContent);
        var options = new JsonSerializerOptions { WriteIndented = true };
        return node?.ToJsonString(options) ?? string.Empty;
    }
}
