// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
    /// Java reference outcome and a present JSON input file. Every in-scope case whose outcome could
    /// NOT be resolved is returned as a skip (with a reason) rather than silently dropped, so the
    /// resulting sample's denominator is never invisibly shrunk.
    /// </summary>
    public static ConformanceLoadResult LoadR4CleanBaseCases()
    {
        var validatorDir = FindValidatorDir();
        var manifestJson = File.ReadAllText(Path.Combine(validatorDir, "manifest.json"));
        var manifest = JsonSerializer.Deserialize<ConformanceManifest>(manifestJson, ManifestOptions)
            ?? throw new InvalidOperationException("Failed to deserialize validator manifest.json.");

        var results = new List<(ConformanceTestCase, ConformanceExpectation)>();
        var skips = new List<ConformanceSkip>();

        foreach (var testCase in manifest.TestCases)
        {
            if (!IsR4CleanBase(testCase, validatorDir))
            {
                continue;
            }

            var (expected, skipReason) = ResolveJavaExpectation(testCase, validatorDir);
            if (expected is not null)
            {
                results.Add((testCase, expected));
            }
            else
            {
                skips.Add(new ConformanceSkip(testCase.Name ?? testCase.File ?? "(unnamed)", skipReason!.Value));
            }
        }

        return new ConformanceLoadResult(results, skips);
    }

    private static bool IsR4CleanBase(ConformanceTestCase testCase, string validatorDir) =>
        ConformanceCaseAnalysis.IsR4CleanBase(
            testCase,
            file => File.Exists(Path.Combine(validatorDir, file)));

    /// <summary>
    /// Resolves the Java reference outcome (inline object or path under <c>outcomes/</c>) into an
    /// expectation. Returns a skip reason instead of throwing when the outcome file is missing or
    /// malformed — a broken vendored fixture should not abort the whole suite.
    /// </summary>
    private static (ConformanceExpectation? Expected, ConformanceSkipReason? Skip) ResolveJavaExpectation(
        ConformanceTestCase testCase, string validatorDir)
    {
        if (testCase.Java is not { } java)
        {
            return (null, ConformanceSkipReason.NoOutcomeField);
        }

        switch (java.ValueKind)
        {
            case JsonValueKind.String:
                var path = Path.Combine(
                    validatorDir,
                    "outcomes",
                    java.GetString()!.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    return (null, ConformanceSkipReason.OutcomeFileMissing);
                }

                var count = ConformanceCaseAnalysis.TryCountErrorsInOutcomeContent(File.ReadAllText(path));
                return count is { } fileCount
                    ? (new ConformanceExpectation(fileCount, "java"), null)
                    : (null, ConformanceSkipReason.OutcomeFileMalformed);

            case JsonValueKind.Object:
                var inlineCount = ConformanceCaseAnalysis.TryCountErrorsInInlineOutcome(java);
                return inlineCount is { } ic
                    ? (new ConformanceExpectation(ic, "java"), null)
                    : (null, ConformanceSkipReason.UnrecognizedOutcomeShape);

            default:
                return (null, ConformanceSkipReason.UnrecognizedOutcomeShape);
        }
    }
}
