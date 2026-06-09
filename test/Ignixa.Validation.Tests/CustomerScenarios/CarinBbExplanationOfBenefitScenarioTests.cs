// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.Validation.Tests.CustomerScenarios;

/// <summary>
/// Evaluates the CARIN-BB ExplanationOfBenefit customer scenario carried over from the
/// legacy Microsoft FHIR Server.
/// <para>
/// The customer submits an <c>ExplanationOfBenefit</c> resource that declares
/// <c>meta.profile = http://hl7.org/fhir/us/carin-bb/StructureDefinition/C4BB-ExplanationOfBenefit-Inpatient-Institutional|2.1.0</c>
/// and expects the validator to surface CARIN-BB profile constraints alongside base FHIR R4 errors.
/// The legacy Firely-based validator produced the baseline OperationOutcome captured under
/// <see cref="FirelyBaselineFile"/>.
/// </para>
/// <para>
/// These tests document what the current Ignixa validator catches with only the
/// base FHIR R4 StructureDefinition loaded (no CARIN-BB IG packages), and explicitly
/// identify the gap that profile-aware validation would close.
/// </para>
/// </summary>
public class CarinBbExplanationOfBenefitScenarioTests
{
    private const string InputFile = "TestData/CustomerScenarios/carin-bb-eob-input.json";
    private const string FirelyBaselineFile = "TestData/CustomerScenarios/carin-bb-eob-firely-baseline.json";

    private readonly ITestOutputHelper _output;
    private readonly ISchema _schema;
    private readonly IValidationSchemaResolver _resolver;

    public CarinBbExplanationOfBenefitScenarioTests(ITestOutputHelper output)
    {
        _output = output;
        _schema = TestSchemaProvider.GetR4Schema();
        var inner = new StructureDefinitionSchemaResolver(_schema);
        _resolver = new CachedValidationSchemaResolver(inner);
    }

    /// <summary>
    /// The customer input contains <c>"code":0300</c> (a numeric literal with a leading zero)
    /// inside <c>item.revenue.coding</c>. Strict JSON parsers reject leading zeros so we sanitize
    /// to a string before exercising the validator. This mirrors the upstream issue the customer
    /// would hit at the API boundary and keeps the validator the unit under test.
    /// </summary>
    private static string LoadSanitizedInput()
    {
        var raw = File.ReadAllText(InputFile);
        return raw.Replace("\"code\":0300", "\"code\":\"0300\"", StringComparison.Ordinal);
    }

    private ValidationResult Validate(ValidationDepth depth)
    {
        var json = JsonNode.Parse(LoadSanitizedInput());
        json.ShouldNotBeNull();
        var source = JsonNodeSourceNode.Create(json!);
        var resourceType = source.ResourceType ?? source.Name;
        var schema = _resolver.GetSchema($"http://hl7.org/fhir/StructureDefinition/{resourceType}");
        schema.ShouldNotBeNull($"Base R4 schema for '{resourceType}' must resolve");

        var settings = new ValidationSettings { Depth = depth };
        return schema!.Validate(source.ToElement(_schema), settings, new ValidationState());
    }

    private void DumpIssues(ValidationResult result, string label)
    {
        _output.WriteLine($"--- {label}: {result.Issues.Count} issue(s) ---");
        foreach (var issue in result.Issues.OrderBy(i => i.Severity).ThenBy(i => i.Path))
        {
            _output.WriteLine($"  [{issue.Severity}] {issue.Code} @ {issue.Path}: {issue.Message}");
        }
    }

    [Fact]
    public void GivenCarinBbEob_WhenInputFixtureLoads_ThenIsExplanationOfBenefit()
    {
        var json = JsonNode.Parse(LoadSanitizedInput());
        json.ShouldNotBeNull();
        var source = JsonNodeSourceNode.Create(json!);
        (source.ResourceType ?? source.Name).ShouldBe("ExplanationOfBenefit");
    }

    [Fact]
    public void GivenCarinBbEob_WhenValidatingAtMinimalDepth_ThenOnlyStructuralIssuesReported()
    {
        var result = Validate(ValidationDepth.Minimal);
        DumpIssues(result, nameof(ValidationDepth.Minimal));

        // Minimal depth runs universal checks only (JsonStructure, IdFormat, Narrative).
        // The input has a well-formed id and no narrative, so this passes today.
        result.IsValid.ShouldBeTrue(
            "Minimal depth should not report schema or invariant violations; got: "
            + string.Join(", ", result.Issues.Select(i => i.Message)));
    }

    [Fact]
    public void GivenCarinBbEob_WhenValidatingAtSpecDepth_ThenDetectsTypeMismatchInProcedureSequence()
    {
        // procedure.sequence is "1" (string) in the customer input but FHIR positiveInt expects a number.
        // This is a base spec violation the type check should catch.
        var result = Validate(ValidationDepth.Spec);
        DumpIssues(result, nameof(ValidationDepth.Spec));

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(
            i => i.Severity == IssueSeverity.Error
                && i.Path.Contains("procedure", StringComparison.Ordinal)
                && i.Path.EndsWith(".sequence", StringComparison.Ordinal),
            customMessage: "Expected procedure.sequence type mismatch (string '1' vs positiveInt) at Spec depth");
    }

    [Fact]
    public void GivenCarinBbEob_WhenValidatingAtFullDepth_ThenRunsBaseSpecFhirPathInvariants()
    {
        // Full depth enables FHIRPath invariants from the base R4 StructureDefinition.
        // ele-1 ("All FHIR elements must have a @value or children") and the dom-* invariants
        // should be evaluated for every element. CARIN-BB profile invariants are NOT in the
        // base spec and remain undetected here - that gap is asserted separately.
        var result = Validate(ValidationDepth.Full);
        DumpIssues(result, nameof(ValidationDepth.Full));

        result.Issues.ShouldNotBeEmpty();
        result.Issues.Count(i => i.Severity == IssueSeverity.Error).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GivenCarinBbEob_WhenValidatingWithProfileLoaded_ThenResolvesProfileSchema()
    {
        // Flipped from "cannot resolve profile today" - profile loading is now wired through
        // PackageResourceProvider + ProfileLayeredSchemaProvider.
        var resolver = await TestHelpers.Packages.CarinBbValidatorFactory.BuildAsync(CancellationToken.None);

        var json = JsonNode.Parse(LoadSanitizedInput())!;
        var source = JsonNodeSourceNode.Create(json);
        var element = source.ToElement(_schema);

        var composed = resolver.ResolveForElement(element);

        composed.ShouldNotBeNull();
        composed!.ResourceType.ShouldBe("ExplanationOfBenefit");
        // With CARIN-BB profile composed in, the schema should carry strictly more checks
        // than the base R4 schema alone.
        var baseSchema = _resolver.GetSchema("http://hl7.org/fhir/StructureDefinition/ExplanationOfBenefit")!;
        composed.Checks.Count.ShouldBeGreaterThan(baseSchema.Checks.Count,
            $"Composed schema (base + CARIN-BB profile) should add checks beyond the base R4 EOB schema. " +
            $"Composed: {composed.Checks.Count}, Base: {baseSchema.Checks.Count}");
    }

    [Fact]
    public void GivenLegacyFirelyOutcome_WhenInspectingErrors_ThenAllErrorsAreCarinBbProfileSpecific()
    {
        // The legacy validator produced 5 errors. Inspect them to verify the categorical claim:
        // every "error"-severity issue references the CARIN-BB profile (StructureDefinition or
        // CARIN-BB-specific ValueSet). This is the evidence backing the "profile loading is
        // required to match parity" conclusion.
        var outcome = JsonNode.Parse(File.ReadAllText(FirelyBaselineFile))!;
        var errors = outcome["issue"]!.AsArray()
            .Where(i => (string?)i!["severity"] == "error")
            .ToList();

        errors.Count.ShouldBe(5);

        foreach (var issue in errors)
        {
            var detailsText = (string?)issue!["details"]?["text"] ?? string.Empty;
            var coding = issue["details"]?["coding"]?.AsArray() ?? new JsonArray();
            var systems = coding.Select(c => (string?)c!["system"] ?? string.Empty).ToList();

            var isCarinBbInvariant = systems.Any(s => s.Contains("carin-bb", StringComparison.OrdinalIgnoreCase));
            var isCarinBbValueSet = detailsText.Contains("carin-bb", StringComparison.OrdinalIgnoreCase);

            (isCarinBbInvariant || isCarinBbValueSet).ShouldBeTrue(
                $"Expected CARIN-BB origin for error; got systems=[{string.Join(",", systems)}], text='{detailsText}'");
        }
    }
}
