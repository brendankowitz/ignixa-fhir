// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.PackageManagement.Models;
using Ignixa.PackageManagement.Validation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Tests.TestHelpers.Packages;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Ignixa.Validation.Tests.CustomerScenarios;

/// <summary>
/// Offline full-profile end-to-end proof against the R4 core <c>bp</c> (blood pressure) profile,
/// loaded from the local FHIR package cache via <see cref="PackageBackedValidator"/> — no network.
/// <c>bp</c> derives from <c>vitalsigns</c> and slices <c>Observation.component</c> by a
/// <c>value</c> discriminator on <c>code.coding.code</c> + <c>code.coding.system</c> into required
/// <c>SystolicBP</c> and <c>DiastolicBP</c> slices. Proves the package-backed setup resolves the
/// profile from <c>meta.profile</c> and enforces its slice cardinality.
/// <para>
/// Requires the R4 core package materialized in the local FHIR cache (present on dev machines,
/// absent on CI, and not distributed as a downloadable tarball). When the cache is present the test
/// fully asserts; when absent it emits a diagnostic and skips (xUnit 2.9.3 has no runtime skip API).
/// </para>
/// </summary>
public sealed class CoreProfileOfflineScenarioTests(ITestOutputHelper output)
{
    private const string BpProfile = "http://hl7.org/fhir/StructureDefinition/bp";

    private static readonly IReadOnlyList<ExtractedResource>? R4Core = LocalFhirPackageLoader.TryLoadR4Core();
    private static readonly R4CoreSchemaProvider Base = new();

    private static readonly PackageBackedValidationSetup? Setup =
        R4Core is null
            ? null
            : PackageBackedValidator.Create(new PackageValidationOptions
            {
                BaseSchemaProvider = Base,
                PackageResources = R4Core,
                ExcludeBaseTypeStructureDefinitions = true,
                LayerPackageValueSets = true,
            });

    private readonly ITestOutputHelper _output = output;

    private static JsonObject ConformantBloodPressure() => new()
    {
        ["resourceType"] = "Observation",
        ["meta"] = new JsonObject { ["profile"] = new JsonArray(BpProfile) },
        ["status"] = "final",
        ["category"] = new JsonArray(new JsonObject
        {
            ["coding"] = new JsonArray(new JsonObject
            {
                ["system"] = "http://terminology.hl7.org/CodeSystem/observation-category",
                ["code"] = "vital-signs",
            }),
        }),
        ["code"] = new JsonObject
        {
            ["coding"] = new JsonArray(new JsonObject
            {
                ["system"] = "http://loinc.org",
                ["code"] = "85354-9",
            }),
        },
        ["subject"] = new JsonObject { ["reference"] = "Patient/example" },
        ["effectiveDateTime"] = "2024-01-01",
        ["component"] = new JsonArray(
            BloodPressureComponent("8480-6", 120),
            BloodPressureComponent("8462-4", 80)),
    };

    private static JsonObject BloodPressureComponent(string loincCode, int value) => new()
    {
        ["code"] = new JsonObject
        {
            ["coding"] = new JsonArray(new JsonObject
            {
                ["system"] = "http://loinc.org",
                ["code"] = loincCode,
            }),
        },
        ["valueQuantity"] = new JsonObject
        {
            ["value"] = value,
            ["unit"] = "mmHg",
            ["system"] = "http://unitsofmeasure.org",
            ["code"] = "mm[Hg]",
        },
    };

    private IElement ToElement(JsonNode json) => JsonNodeSourceNode.Create(json).ToElement(Base);

    private static IReadOnlyList<ValidationIssue> SlicingIssues(ValidationResult result)
        => result.Issues.Where(i => i.Code.StartsWith("slicing", StringComparison.Ordinal)).ToList();

    private void Dump(ValidationResult result, string label)
    {
        _output.WriteLine($"--- {label} ---");
        foreach (var i in SlicingIssues(result))
        {
            _output.WriteLine($"  [{i.Severity}] {i.Code} @ {i.Path}: {i.Message}");
        }
    }

    [Fact]
    public void GivenConformantBloodPressure_WhenValidatedThroughPackageBackedSetupAtFullDepth_ThenProfileResolvesAndNoSlicingErrors()
    {
        // Environment-dependent: needs the R4 core package in the local FHIR cache (present on dev
        // machines, absent on CI, and not downloadable via TestFhirPackageLoader). xUnit 2.9.3 has no
        // runtime Assert.Skip, so skip with a diagnostic line rather than fail the build off-cache.
        if (Setup is null)
        {
            _output.WriteLine("R4 core package not in local FHIR cache — skipping offline profile e2e.");
            return;
        }

        var element = ToElement(ConformantBloodPressure());

        // The profile resolves from meta.profile through the package-backed layered resolver.
        var schema = Setup!.SchemaResolver.ResolveForElement(element);
        schema.ShouldNotBeNull();

        var result = schema!.Validate(element, new ValidationSettings { Depth = ValidationDepth.Full }, new ValidationState());

        Dump(result, "conformant blood-pressure Observation");
        SlicingIssues(result)
            .Where(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal)
            .ShouldBeEmpty("a conformant blood pressure must satisfy both required component slices");
    }

    [Fact]
    public void GivenBloodPressureMissingDiastolicComponent_WhenValidatedAtFullDepth_ThenRejectedNamingDiastolicSlice()
    {
        if (Setup is null)
        {
            _output.WriteLine("R4 core package not in local FHIR cache — skipping offline profile e2e.");
            return;
        }

        var json = ConformantBloodPressure();

        // Drop the diastolic component: the DiastolicBP slice is 1..1, so this violates slice min.
        var components = (json["component"] as JsonArray)!;
        components.RemoveAt(1);

        var element = ToElement(json);
        var schema = Setup!.SchemaResolver.ResolveForElement(element);
        schema.ShouldNotBeNull();

        var result = schema!.Validate(element, new ValidationSettings { Depth = ValidationDepth.Full }, new ValidationState());

        Dump(result, "blood-pressure missing diastolic component");
        SlicingIssues(result).ShouldContain(
            i => i.Code == "slicing-cardinality"
                && i.Severity == IssueSeverity.Error
                && i.Message.Contains("Diastolic", StringComparison.OrdinalIgnoreCase),
            "a blood pressure missing its diastolic component must be rejected against the 'DiastolicBP' slice");
    }
}
