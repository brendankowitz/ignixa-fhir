// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Tests.TestHelpers.Packages;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.Validation.Tests.CustomerScenarios;

/// <summary>
/// End-to-end validation scenarios against the US Core 6.1.0 IG. Mirrors the CARIN-BB
/// scenario tests but exercises a different IG to prove the validator chain is IG-agnostic.
/// </summary>
public class UsCorePatientScenarioTests
{
    private const string ValidInputFile = "TestData/CustomerScenarios/us-core-patient-valid.json";
    private const string MissingRequiredInputFile = "TestData/CustomerScenarios/us-core-patient-missing-required.json";
    private const string ProfileCanonical = "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient";

    private readonly ITestOutputHelper _output;
    private readonly ISchema _schema = new R4CoreSchemaProvider();

    public UsCorePatientScenarioTests(ITestOutputHelper output) => _output = output;

    private void DumpIssues(ValidationResult result, string label)
    {
        _output.WriteLine($"--- {label}: {result.Issues.Count} issue(s) ---");
        foreach (var i in result.Issues.OrderBy(i => i.Severity).ThenBy(i => i.Path))
        {
            _output.WriteLine($"  [{i.Severity}] {i.Code} @ {i.Path}: {i.Message}");
        }
    }

    [Fact]
    public async Task GivenUsCorePackage_WhenAdaptingUsCorePatientProfile_ThenRootIsPatient()
    {
        // Pinpoints the adapter path independently of the full validator chain.
        var pkg = await TestFhirPackageLoader.LoadUsCoreAsync(CancellationToken.None);
        var sd = pkg.FindByCanonical(ProfileCanonical);
        sd.ShouldNotBeNull();

        var provider = new PackageResourceProvider(NullLogger<PackageResourceProvider>.Instance);
        var type = provider.ToTypeDefinition(sd.ResourceJson, "4.0.1");

        type.ShouldNotBeNull();
        type!.Info.Name.ShouldBe("Patient");
        type.Info.IsResource.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenUsCorePatientResource_WhenMetaProfileDeclaresUsCore_ThenResolverComposesProfileSchema()
    {
        var resolver = await UsCoreValidatorFactory.BuildAsync(CancellationToken.None);

        var json = JsonNode.Parse(await File.ReadAllTextAsync(ValidInputFile, CancellationToken.None));
        var source = JsonNodeSourceNode.Create(json!);
        var element = source.ToElement(_schema);

        var composed = resolver.ResolveForElement(element);

        composed.ShouldNotBeNull();
        composed!.ResourceType.ShouldBe("Patient");

        // Composed schema (base R4 + us-core-patient profile) should carry strictly more
        // checks than the base R4 Patient schema alone.
        var basePatientSchema = new StructureDefinitionSchemaResolver(_schema)
            .GetSchema("http://hl7.org/fhir/StructureDefinition/Patient");
        basePatientSchema.ShouldNotBeNull();
        composed.Checks.Count.ShouldBeGreaterThan(basePatientSchema!.Checks.Count,
            $"Composed schema should add checks beyond base R4. Composed: {composed.Checks.Count}, " +
            $"Base: {basePatientSchema.Checks.Count}");
    }

    [Fact]
    public async Task GivenWellFormedUsCorePatient_WhenValidatingAtSpecDepth_ThenNoErrors()
    {
        var resolver = await UsCoreValidatorFactory.BuildAsync(CancellationToken.None);

        var json = JsonNode.Parse(await File.ReadAllTextAsync(ValidInputFile, CancellationToken.None));
        var source = JsonNodeSourceNode.Create(json!);
        var element = source.ToElement(_schema);
        var schema = resolver.ResolveForElement(element);
        schema.ShouldNotBeNull();

        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var result = schema!.Validate(element, settings, new ValidationState());

        DumpIssues(result, "valid Patient @ Spec");
        result.Issues.Where(i => i.Severity == IssueSeverity.Error).ShouldBeEmpty(
            "A well-formed US Core Patient should not produce error-severity issues at Spec depth");
    }

    [Fact]
    public async Task GivenUsCorePatientMissingRequiredFields_WhenValidatingAtSpecDepth_ThenReportsCardinalityErrors()
    {
        var resolver = await UsCoreValidatorFactory.BuildAsync(CancellationToken.None);

        var json = JsonNode.Parse(await File.ReadAllTextAsync(MissingRequiredInputFile, CancellationToken.None));
        var source = JsonNodeSourceNode.Create(json!);
        var element = source.ToElement(_schema);
        var schema = resolver.ResolveForElement(element);
        schema.ShouldNotBeNull();

        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        var result = schema!.Validate(element, settings, new ValidationState());

        DumpIssues(result, "missing-required Patient @ Spec");

        // US Core Patient sets min=1 on identifier, name, and gender. Cardinality checks
        // should fire for each, regardless of whether other US Core requirements (race/ethnicity
        // extensions etc) are also flagged.
        result.IsValid.ShouldBeFalse();
        var failingPaths = result.Issues
            .Where(i => i.Severity == IssueSeverity.Error)
            .Select(i => i.Path)
            .ToList();

        failingPaths.ShouldContain(p => p.Contains("identifier", StringComparison.Ordinal),
            customMessage: "US Core requires Patient.identifier (min=1)");
        failingPaths.ShouldContain(p => p.Contains("name", StringComparison.Ordinal),
            customMessage: "US Core requires Patient.name (min=1)");
        failingPaths.ShouldContain(p => p.Contains("gender", StringComparison.Ordinal),
            customMessage: "US Core requires Patient.gender (min=1)");
    }
}
