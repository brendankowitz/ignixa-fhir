// <copyright file="ConformanceCaseLoader.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json;

namespace Ignixa.Validation.Tests.Conformance;

/// <summary>
/// Locates the vendored official FHIR validator test suite, parses the manifest, filters to the
/// R4 clean-base slice, and resolves reference-validator expected outcomes.
/// See <see cref="ValidatorConformanceRunner"/> and docs/features/validation/roadmap.md.
/// </summary>
public static class ConformanceCaseLoader
{
    private const string RelativeValidatorDir =
        "test/Ignixa.FhirPath.Tests/TestData/fhir-test-cases/validator";

    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Resolves the absolute path to the vendored <c>validator/</c> directory by walking up from the
    /// test output directory to the repo root.
    /// </summary>
    public static string FindValidatorDir()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(
                current,
                RelativeValidatorDir.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                return candidate;
            }

            var parent = Path.GetDirectoryName(current);
            if (parent == current)
            {
                break;
            }

            current = parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate vendored FHIR validator test cases ('{RelativeValidatorDir}') above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    /// Loads the manifest and returns the R4 "clean base" cases — those validatable against the base
    /// spec with no IG packages, supporting resources, or explicit profiles — that have a resolvable
    /// Java reference outcome and a present JSON input file.
    /// </summary>
    public static IReadOnlyList<(ConformanceTestCase Case, ConformanceExpectation Expected)> LoadR4CleanBaseCases()
    {
        var validatorDir = FindValidatorDir();
        var manifestJson = File.ReadAllText(Path.Combine(validatorDir, "manifest.json"));
        var manifest = JsonSerializer.Deserialize<ConformanceManifest>(manifestJson, ManifestOptions)
            ?? throw new InvalidOperationException("Failed to deserialize validator manifest.json.");

        var results = new List<(ConformanceTestCase, ConformanceExpectation)>();
        foreach (var testCase in manifest.TestCases)
        {
            if (!IsR4CleanBase(testCase, validatorDir))
            {
                continue;
            }

            var expected = TryResolveJavaExpectation(testCase, validatorDir);
            if (expected is not null)
            {
                results.Add((testCase, expected));
            }
        }

        return results;
    }

    private static bool IsR4CleanBase(ConformanceTestCase c, string validatorDir)
    {
        if (c.Version != "4.0" || !c.UseTest)
        {
            return false;
        }

        if (c.File is null || !c.File.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (c.Packages is { Count: > 0 } || c.Supporting is { Count: > 0 }
            || c.Profiles is { Count: > 0 } || c.Profile is not null || c.Logical is not null)
        {
            return false;
        }

        return File.Exists(Path.Combine(validatorDir, c.File));
    }

    /// <summary>
    /// Resolves the Java reference outcome (inline object or path under <c>outcomes/</c>) into an
    /// expectation. Returns null when no usable Java outcome is available.
    /// </summary>
    private static ConformanceExpectation? TryResolveJavaExpectation(ConformanceTestCase c, string validatorDir)
    {
        if (c.Java is not { } java)
        {
            return null;
        }

        int? errorCount = java.ValueKind switch
        {
            JsonValueKind.String => CountErrorsInOutcomeFile(validatorDir, java.GetString()!),
            JsonValueKind.Object => CountErrorsInInlineOutcome(java),
            _ => null,
        };

        return errorCount is { } count
            ? new ConformanceExpectation(count == 0, count, "java")
            : null;
    }

    private static int? CountErrorsInInlineOutcome(JsonElement inline)
    {
        if (inline.TryGetProperty("errorCount", out var ec) && ec.TryGetInt32(out var count))
        {
            return count;
        }

        // Some entries embed a nested OperationOutcome under "outcome" instead of a count.
        if (inline.TryGetProperty("outcome", out var outcome) && outcome.ValueKind == JsonValueKind.Object)
        {
            return CountErrorIssues(outcome);
        }

        return null;
    }

    private static int? CountErrorsInOutcomeFile(string validatorDir, string relativePath)
    {
        var path = Path.Combine(
            validatorDir,
            "outcomes",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return CountErrorIssues(doc.RootElement);
    }

    private static int CountErrorIssues(JsonElement operationOutcome)
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
