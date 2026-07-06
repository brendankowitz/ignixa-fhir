// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.Validation.Tests.Conformance;

/// <summary>
/// Pure filtering and outcome-counting logic used by <see cref="ConformanceCaseLoader"/>, extracted
/// so it is unit-testable against synthetic <see cref="JsonElement"/>/JSON fixtures without touching
/// the vendored test-case directory on disk. All I/O (manifest/outcome file reads) stays in
/// <see cref="ConformanceCaseLoader"/>; this class only ever inspects already-loaded data.
/// </summary>
internal static class ConformanceCaseAnalysis
{
    /// <summary>
    /// Determines whether a manifest entry belongs to the R4 "clean base" slice: validatable against
    /// the base spec alone, with no IG packages, supporting resources, or explicit profile/logical
    /// configuration, and whose input JSON file is present.
    /// </summary>
    /// <param name="testCase">The manifest entry to classify.</param>
    /// <param name="fileExists">
    /// Predicate reporting whether <paramref name="testCase"/>'s <c>File</c> exists on disk. Injected
    /// so this method stays pure/testable; the loader supplies the real filesystem check.
    /// </param>
    /// <returns>True when the entry is an R4 clean-base case.</returns>
    public static bool IsR4CleanBase(ConformanceTestCase testCase, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        ArgumentNullException.ThrowIfNull(fileExists);

        if (testCase.Version != "4.0" || !testCase.UseTest)
        {
            return false;
        }

        if (testCase.File is null || !testCase.File.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (testCase.Packages is { Count: > 0 } || testCase.Supporting is { Count: > 0 }
            || testCase.Profiles is { Count: > 0 } || testCase.Profile is not null || testCase.Logical is not null)
        {
            return false;
        }

        return fileExists(testCase.File);
    }

    /// <summary>
    /// Counts error/fatal issues from an inline <c>java</c> outcome object: either an <c>errorCount</c>
    /// property, or a nested <c>outcome</c> OperationOutcome.
    /// </summary>
    /// <param name="inline">The inline JSON object from the manifest's <c>java</c> property.</param>
    /// <returns>The error count, or null when neither recognized shape is present.</returns>
    public static int? TryCountErrorsInInlineOutcome(JsonElement inline)
    {
        if (inline.TryGetProperty("errorCount", out var ec) && ec.TryGetInt32(out var count))
        {
            return count;
        }

        if (inline.TryGetProperty("outcome", out var outcome) && outcome.ValueKind == JsonValueKind.Object)
        {
            return CountErrorIssues(outcome);
        }

        return null;
    }

    /// <summary>
    /// Parses an outcome file's raw JSON content and counts its error/fatal issues.
    /// </summary>
    /// <param name="json">The outcome file's raw JSON content.</param>
    /// <returns>The error count, or null when the content is not parseable JSON.</returns>
    public static int? TryCountErrorsInOutcomeContent(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var doc = JsonDocument.Parse(json);
            return CountErrorIssues(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Counts issues at error/fatal severity within an OperationOutcome's <c>issue</c> array.
    /// </summary>
    /// <param name="operationOutcome">The OperationOutcome JSON element.</param>
    /// <returns>The number of error/fatal issues. Zero when <c>issue</c> is absent or not an array.</returns>
    public static int CountErrorIssues(JsonElement operationOutcome)
    {
        if (!operationOutcome.TryGetProperty("issue", out var issues) || issues.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var count = 0;
        foreach (var issue in issues.EnumerateArray())
        {
            if (issue.TryGetProperty("severity", out var sev)
                && sev.ValueKind == JsonValueKind.String
                && sev.GetString() is "error" or "fatal")
            {
                count++;
            }
        }

        return count;
    }
}
