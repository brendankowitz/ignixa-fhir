// <copyright file="ValidatorConformanceRunner.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.Validation.Tests.Conformance;

/// <summary>
/// Runs Ignixa validation against the official HL7 FHIR validator test suite and reports the pass
/// rate versus the Java reference validator. Observational (non-gating): it establishes a baseline
/// and emits a triage report bucketed by mismatch direction and module, so feature work can be
/// ordered by where we actually diverge. See docs/features/validation/roadmap.md (Phase 1).
/// </summary>
public sealed class ValidatorConformanceRunner(ITestOutputHelper output)
{
    private static readonly ISchema Schema = new R4CoreSchemaProvider();

    private static readonly IValidationSchemaResolver Resolver =
        new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(Schema));

    private readonly ITestOutputHelper _output = output;

    [Fact]
    [Trait("Category", "Conformance")]
    public void Baseline_R4_CleanBase_AgainstJavaReference()
    {
        var validatorDir = ConformanceCaseLoader.FindValidatorDir();
        var cases = ConformanceCaseLoader.LoadR4CleanBaseCases();
        cases.ShouldNotBeEmpty();

        var mismatches = new List<TriageRow>();
        var passed = 0;

        foreach (var (testCase, expected) in cases)
        {
            var inputPath = Path.Combine(validatorDir, testCase.File!);
            var actualValid = TryValidate(inputPath, out var errorCount, out var firstError);

            if (actualValid == expected.ExpectedValid)
            {
                passed++;
            }
            else
            {
                mismatches.Add(new TriageRow(
                    testCase.Name ?? testCase.File!,
                    testCase.Module ?? "(none)",
                    expected.ExpectedValid,
                    actualValid,
                    expected.ExpectedErrorCount,
                    errorCount,
                    firstError));
            }
        }

        WriteReport(cases.Count, passed, mismatches);

        // Observational baseline — do not fail on pass rate. Guard only that the suite actually ran.
        cases.Count.ShouldBeGreaterThan(0);
    }

    private static bool TryValidate(string inputPath, out int errorCount, out string? firstError)
    {
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(inputPath));
            if (json is null)
            {
                errorCount = 1;
                firstError = "empty/null JSON";
                return false;
            }

            var sourceNode = JsonNodeSourceNode.Create(json);
            var resourceType = sourceNode.ResourceType ?? sourceNode.Name;
            var schema = Resolver.GetSchema($"http://hl7.org/fhir/StructureDefinition/{resourceType}");
            if (schema is null)
            {
                errorCount = 1;
                firstError = $"no schema for resourceType '{resourceType}'";
                return false;
            }

            var settings = new ValidationSettings { Depth = ValidationDepth.Full };
            var result = schema.Validate(sourceNode.ToElement(Schema), settings, new ValidationState());
            var errors = result.Issues
                .Where(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal)
                .ToList();
            errorCount = errors.Count;
            firstError = errors.Count > 0 ? errors[0].Message : null;
            return errorCount == 0;
        }
        catch (Exception ex)
        {
            // Parse/navigation failures count as "invalid" — the reference validator rejects these too.
            errorCount = 1;
            firstError = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private void WriteReport(int total, int passed, List<TriageRow> mismatches)
    {
        var passRate = total == 0 ? 0 : 100.0 * passed / total;

        // Over-strict: we report errors the reference accepts. Under-strict: we miss errors it catches.
        var overStrict = mismatches.Count(r => !r.ActualValid && r.ExpectedValid);
        var underStrict = mismatches.Count(r => r.ActualValid && !r.ExpectedValid);

        var summary = new StringBuilder();
        summary.AppendLine("=== Ignixa R4 clean-base conformance vs Java reference ===");
        summary.AppendLine(
            $"Total: {total}  Passed: {passed}  Failed: {mismatches.Count}  Pass rate: {passRate:F1}%");
        summary.AppendLine($"Over-strict  (we reject, ref accepts): {overStrict}");
        summary.AppendLine($"Under-strict (we accept, ref rejects): {underStrict}");
        summary.AppendLine();
        summary.AppendLine("Failures by module:");
        foreach (var group in mismatches.GroupBy(r => r.Module).OrderByDescending(g => g.Count()))
        {
            summary.AppendLine($"  {group.Key,-16} {group.Count()}");
        }

        var csvPath = Path.Combine(AppContext.BaseDirectory, "conformance-triage-r4.csv");
        File.WriteAllText(csvPath, BuildTriageCsv(mismatches));
        summary.AppendLine();
        summary.AppendLine($"Triage CSV: {csvPath}");

        var text = summary.ToString();
        _output.WriteLine(text);
        Console.WriteLine(text);
    }

    private static string BuildTriageCsv(List<TriageRow> mismatches)
    {
        var csv = new StringBuilder();
        csv.AppendLine("name,module,expectedValid,actualValid,expectedErrors,actualErrors,direction,firstError");
        foreach (var r in mismatches.OrderBy(r => r.Module).ThenBy(r => r.Name))
        {
            var direction = !r.ActualValid && r.ExpectedValid ? "over-strict" : "under-strict";
            csv.AppendLine(string.Join(
                ',',
                Csv(r.Name),
                Csv(r.Module),
                r.ExpectedValid,
                r.ActualValid,
                r.ExpectedErrorCount,
                r.ActualErrorCount,
                direction,
                Csv(r.FirstError ?? string.Empty)));
        }

        return csv.ToString();
    }

    private static string Csv(string value)
    {
        var escaped = value.Replace("\r", " ").Replace("\n", " ").Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private sealed record TriageRow(
        string Name,
        string Module,
        bool ExpectedValid,
        bool ActualValid,
        int ExpectedErrorCount,
        int ActualErrorCount,
        string? FirstError);
}
