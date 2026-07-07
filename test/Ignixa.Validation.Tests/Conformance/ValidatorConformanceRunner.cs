// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.PackageManagement.Models;
using Ignixa.PackageManagement.Validation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers.Packages;
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
    private static readonly R4CoreSchemaProvider BaseProvider = new();
    private static readonly ISchema Schema = BaseProvider;

    // Load the R4 core package from the local FHIR cache (offline) so core extensions and
    // CodeSystems RESOLVE through the layered setup. Base-type StructureDefinitions are EXCLUDED and
    // package ValueSets are NOT layered, so the scored base-schema + ValidateCode path stays
    // byte-identical to base-only validation — the hard zero-over-strict gate holds. The package only
    // ADDS resolvable extension StructureDefinitions and CodeSystem content, proven by the diagnostic
    // R4CorePackage_ResolvesExtensionsAndCodeSystems below. Null when the package is absent (CI
    // without the cache), in which case the runner falls back to the base-only resolver.
    private static readonly IReadOnlyList<ExtractedResource>? R4CorePackage = LocalFhirPackageLoader.TryLoadR4Core();

    private static readonly PackageBackedValidationSetup? PackageSetup =
        R4CorePackage is null
            ? null
            : PackageBackedValidator.Create(new PackageValidationOptions
            {
                BaseSchemaProvider = BaseProvider,
                PackageResources = R4CorePackage,
                ExcludeBaseTypeStructureDefinitions = true,
                LayerPackageValueSets = false,
            });

    private static readonly IValidationSchemaResolver Resolver =
        (IValidationSchemaResolver?)PackageSetup?.SchemaResolver
        ?? new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(Schema));

    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// Offline resolution proof for the package-backed setup: with the R4 core package loaded, a core
    /// extension StructureDefinition resolves by canonical/id, a base type still resolves to the
    /// generated (un-shadowed) schema, and a core CodeSystem code resolves to its display through the
    /// terminology <c>$lookup</c> surface. This proves resolution WORKS without acting on it (no new
    /// checks). Skips when the package is not in the local FHIR cache.
    /// </summary>
    [Fact]
    [Trait("Category", "Conformance")]
    public async Task R4CorePackage_ResolvesExtensionsAndCodeSystems()
    {
        if (PackageSetup is null)
        {
            _output.WriteLine("R4 core package not present in local FHIR cache — skipping resolution proof.");
            return;
        }

        // Extension StructureDefinition resolves — by id on the schema provider and by canonical
        // through the resolver used for validation.
        PackageSetup.SchemaProvider.GetTypeDefinition("patient-birthTime").ShouldNotBeNull();
        Resolver.GetSchema("http://hl7.org/fhir/StructureDefinition/patient-birthTime").ShouldNotBeNull();
        Resolver.GetSchema("http://hl7.org/fhir/StructureDefinition/data-absent-reason").ShouldNotBeNull();

        // Base type still resolves to the generated schema — the package did not shadow it.
        Resolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient").ShouldNotBeNull();

        // CodeSystem code resolves to its display through the terminology $lookup surface.
        PackageSetup.CodeSystemProvider
            .GetDisplay("http://hl7.org/fhir/administrative-gender", "male")
            .ShouldBe("Male");
        var lookup = await PackageSetup.TerminologyService
            .LookupCodeAsync("http://hl7.org/fhir/administrative-gender", "male", version: null, CancellationToken.None);
        lookup.Found.ShouldBeTrue();
        lookup.Display.ShouldBe("Male");
    }

    [Fact]
    [Trait("Category", "Conformance")]
    public void Baseline_R4_CleanBase_AgainstJavaReference()
    {
        var validatorDir = ConformanceCaseLoader.FindValidatorDir();
        var loadResult = ConformanceCaseLoader.LoadR4CleanBaseCases();
        loadResult.Cases.ShouldNotBeEmpty();

        var mismatches = new List<TriageRow>();
        var errored = new List<ErroredRow>();
        var passed = 0;

        foreach (var (testCase, expected) in loadResult.Cases)
        {
            var inputPath = Path.Combine(validatorDir, testCase.File!);
            var outcome = TryValidate(inputPath, testCase, out var errorCount, out var firstError);

            if (outcome == ValidationOutcome.Errored)
            {
                errored.Add(new ErroredRow(
                    testCase.Name ?? testCase.File!,
                    testCase.Module ?? "(none)",
                    firstError ?? "(no message)"));
                continue;
            }

            var actualValid = outcome == ValidationOutcome.Valid;
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

        WriteReport(loadResult, passed, mismatches, errored);

        // Observational baseline — do not fail on pass rate. Guard only that the suite actually ran.
        loadResult.Cases.Count.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Outcome bucket for a single conformance attempt. <see cref="Errored"/> is kept separate from
    /// <see cref="Valid"/>/<see cref="Invalid"/> so an Ignixa pipeline bug never masquerades as a
    /// pass/fail verdict against the reference validator.
    /// </summary>
    private enum ValidationOutcome
    {
        Valid,
        Invalid,
        Errored,
    }

    private static ValidationOutcome TryValidate(string inputPath, ConformanceTestCase testCase, out int errorCount, out string? firstError)
    {
        try
        {
            // Honour the JSON5 allow-comments flag: only cases that opt in tolerate // comments; for
            // every other case a comment is still a JsonException (a genuinely malformed resource).
            var documentOptions = new JsonDocumentOptions
            {
                CommentHandling = testCase.AllowComments ? JsonCommentHandling.Skip : JsonCommentHandling.Disallow,
            };
            var json = JsonNode.Parse(File.ReadAllText(inputPath), documentOptions: documentOptions);
            if (json is null)
            {
                errorCount = 1;
                firstError = "empty/null JSON";
                return ValidationOutcome.Invalid;
            }

            var sourceNode = JsonNodeSourceNode.Create(json);
            var resourceType = sourceNode.ResourceType ?? sourceNode.Name;
            var schema = Resolver.GetSchema($"http://hl7.org/fhir/StructureDefinition/{resourceType}");
            if (schema is null)
            {
                errorCount = 1;
                firstError = $"no schema for resourceType '{resourceType}'";
                return ValidationOutcome.Invalid;
            }

            var settings = new ValidationSettings
            {
                Depth = ValidationDepth.Full,
                SecurityChecks = testCase.SecurityChecks,
                NoHtmlInMarkdown = testCase.NoHtmlInMarkdown,

                // examples: only an explicit `false` turns the example-URL check ON. Absent or true
                // (spec mode) leaves it off, so the many resources that legitimately carry example.org
                // URLs are unaffected and never flip to over-strict.
                CheckExampleUrls = testCase.Examples == false,

                // validateContains: IGNORE skips contained-resource validation.
                ValidateContainedResources =
                    !string.Equals(testCase.ValidateContains, "IGNORE", StringComparison.OrdinalIgnoreCase),
            };

            // Seed tree-context scope exactly as the production handler does (ValidateResourceHandler),
            // so %resource / %rootResource / resolve() engage for dom-*/bdl-* invariants and reference
            // resolution. Without this, invariant evaluation falls back to context-free mode.
            var element = sourceNode.ToElement(Schema);
            var state = new ValidationState().EnterRootResource(element);
            var result = schema.Validate(element, settings, state);
            var errors = result.Issues
                .Where(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal)
                .ToList();
            errorCount = errors.Count;
            firstError = errors.Count > 0 ? errors[0].Message : null;
            return errorCount == 0 ? ValidationOutcome.Valid : ValidationOutcome.Invalid;
        }
        catch (JsonException ex)
        {
            // Malformed input JSON is a genuinely invalid resource — the reference validator rejects
            // these too, so it belongs in the pass/fail tally, not the errored bucket.
            errorCount = 1;
            firstError = $"{ex.GetType().Name}: {ex.Message}";
            return ValidationOutcome.Invalid;
        }
        catch (Exception ex)
        {
            // An unexpected pipeline/engine exception (NullReferenceException, InvalidOperationException,
            // etc.) is a defect in OUR validator, not a verdict about the resource. Scoring it as
            // "invalid" would let an engine bug masquerade as a correct rejection on expected-invalid
            // cases, and mischarge it as "over-strict" on expected-valid cases. Bucket it separately.
            errorCount = 0;
            firstError = $"{ex.GetType().Name}: {ex.Message}";
            return ValidationOutcome.Errored;
        }
    }

    private void WriteReport(
        ConformanceLoadResult loadResult,
        int passed,
        List<TriageRow> mismatches,
        List<ErroredRow> errored)
    {
        var total = loadResult.Cases.Count;
        var attempted = total - errored.Count;
        var passRate = attempted == 0 ? 0 : 100.0 * passed / attempted;

        // Over-strict: we report errors the reference accepts. Under-strict: we miss errors it catches.
        var overStrict = mismatches.Count(r => !r.ActualValid && r.ExpectedValid);
        var underStrict = mismatches.Count(r => r.ActualValid && !r.ExpectedValid);

        var summary = new StringBuilder();
        summary.AppendLine("=== Ignixa R4 clean-base conformance vs Java reference ===");
        summary.AppendLine(
            $"Total: {total}  Passed: {passed}  Failed: {mismatches.Count}  Errored: {errored.Count}  Pass rate (of attempted): {passRate:F1}%");
        summary.AppendLine($"Over-strict  (we reject, ref accepts): {overStrict}");
        summary.AppendLine($"Under-strict (we accept, ref rejects): {underStrict}");
        summary.AppendLine($"Skipped (in-scope, outcome unresolved): {loadResult.Skips.Count}");
        summary.AppendLine();

        summary.AppendLine("Failures by module:");
        foreach (var group in mismatches.GroupBy(r => r.Module).OrderByDescending(g => g.Count()))
        {
            summary.AppendLine($"  {group.Key,-16} {group.Count()}");
        }

        summary.AppendLine();
        summary.AppendLine("Errored by exception type:");
        foreach (var group in errored.GroupBy(r => ExceptionTypeOf(r.FirstError)).OrderByDescending(g => g.Count()))
        {
            summary.AppendLine($"  {group.Key,-24} {group.Count()}");
        }

        summary.AppendLine();
        summary.AppendLine("Skipped by reason:");
        foreach (var group in loadResult.Skips.GroupBy(s => s.Reason).OrderByDescending(g => g.Count()))
        {
            summary.AppendLine($"  {group.Key,-24} {group.Count()}");
        }

        var csvPath = Path.Combine(AppContext.BaseDirectory, "conformance-triage-r4.csv");
        File.WriteAllText(csvPath, BuildTriageCsv(mismatches));
        summary.AppendLine();
        summary.AppendLine($"Triage CSV: {csvPath}");

        var erroredCsvPath = Path.Combine(AppContext.BaseDirectory, "conformance-errored-r4.csv");
        File.WriteAllText(erroredCsvPath, BuildErroredCsv(errored));
        summary.AppendLine($"Errored CSV: {erroredCsvPath}");

        var text = summary.ToString();
        _output.WriteLine(text);
        Console.WriteLine(text);
    }

    private static string ExceptionTypeOf(string firstError)
    {
        var separator = firstError.IndexOf(':', StringComparison.Ordinal);
        return separator < 0 ? firstError : firstError[..separator];
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

    private static string BuildErroredCsv(List<ErroredRow> errored)
    {
        var csv = new StringBuilder();
        csv.AppendLine("name,module,exceptionType,message");
        foreach (var r in errored.OrderBy(r => r.Module).ThenBy(r => r.Name))
        {
            csv.AppendLine(string.Join(
                ',',
                Csv(r.Name),
                Csv(r.Module),
                Csv(ExceptionTypeOf(r.FirstError)),
                Csv(r.FirstError)));
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

    private sealed record ErroredRow(string Name, string Module, string FirstError);
}
