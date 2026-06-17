// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.EdgeCases;
using Ignixa.FhirFakes.EdgeCases.Strategies;
using Ignixa.Serialization.SourceNodes;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.EdgeCases;

public class StringBoundaryStrategyTests
{
    private const string SampleJson = """
        {
          "resourceType": "Patient",
          "id": "sb-test",
          "gender": "male",
          "birthDate": "1990-01-01",
          "name": [
            { "family": "Doe", "given": ["Jane"] }
          ],
          "identifier": [
            { "system": "http://example.org/mrn", "value": "MRN-999", "type": { "coding": [{ "code": "MR", "system": "http://hl7.org/fhir/v2/0203" }] } }
          ]
        }
        """;

    // ── Family / Intent metadata ──────────────────────────────────────────────

    [Theory]
    [InlineData("string.max-length", ValidityIntent.PreservesValidity)]
    [InlineData("string.injection-like", ValidityIntent.PreservesValidity)]
    [InlineData("string.control-chars", ValidityIntent.MayViolate)]
    [InlineData("string.empty-present", ValidityIntent.AlwaysInvalid)]
    [InlineData("string.whitespace-only", ValidityIntent.MayViolate)]
    public void GivenStringStrategy_WhenInspectingMetadata_ThenFamilyIsStringBoundaryAndIntentIsCorrect(
        string category, ValidityIntent expectedIntent)
    {
        var strategy = ResolveExact(category);

        strategy.Family.ShouldBe(EdgeCaseFamily.StringBoundary);
        strategy.Intent.ShouldBe(expectedIntent);
    }

    // ── Catalog resolution ────────────────────────────────────────────────────

    [Fact]
    public void GivenDefaultCatalog_WhenResolvingStringFamily_ThenReturnsFiveStrategies()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var resolved = catalog.Resolve(["string"]);

        resolved.Count.ShouldBe(5);
        resolved.ShouldAllBe(s => s.Family == EdgeCaseFamily.StringBoundary);
    }

    [Fact]
    public void GivenDefaultCatalog_WhenResolvingSpecificStringCategory_ThenReturnsExactlyOne()
    {
        var catalog = EdgeCaseCatalog.CreateDefault();

        var resolved = catalog.Resolve(["string.max-length"]);

        resolved.Count.ShouldBe(1);
        resolved[0].Category.ShouldBe("string.max-length");
    }

    // ── Default pipeline (PreservesValidity only) ─────────────────────────────

    [Fact]
    public void GivenDefaultPipeline_WhenStringStrategiesApplied_ThenValidityPreservingStrategiesFire()
    {
        var strategies = EdgeCaseCatalog.CreateDefault().Resolve(["string"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        var manifest = new EdgeCasePipeline(42, EdgeCaseTargetFactory.Schema).Apply(resource, strategies);

        manifest.Mutations.ShouldContain(m => m.Category == "string.max-length" || m.Category == "string.injection-like");
    }

    [Fact]
    public void GivenDefaultPipeline_WhenStringStrategiesApplied_ThenMayViolateStrategiesNeverFire()
    {
        var strategies = EdgeCaseCatalog.CreateDefault().Resolve(["string"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        var manifest = new EdgeCasePipeline(42, EdgeCaseTargetFactory.Schema).Apply(resource, strategies);

        manifest.Mutations.ShouldNotContain(m => m.Category == "string.control-chars" || m.Category == "string.empty-present" || m.Category == "string.whitespace-only");
    }

    // ── includeNonValidityPreserving overload ─────────────────────────────────

    [Fact]
    public void GivenPipelineWithIncludeInvalid_WhenStringStrategiesApplied_ThenMayViolateStrategiesMayFire()
    {
        var strategies = EdgeCaseCatalog.CreateDefault().Resolve(["string"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        var manifest = new EdgeCasePipeline(42, EdgeCaseTargetFactory.Schema).Apply(resource, strategies, includeNonValidityPreserving: true);

        manifest.Mutations.ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenPipelineWithIncludeInvalidFalse_WhenApplied_ThenBehaviourIdenticalToDefaultOverload()
    {
        var strategies = EdgeCaseCatalog.CreateDefault().Resolve(["string"]);

        var r1 = ResourceJsonNode.Parse(SampleJson);
        var m1 = new EdgeCasePipeline(99, EdgeCaseTargetFactory.Schema).Apply(r1, strategies);

        var r2 = ResourceJsonNode.Parse(SampleJson);
        var m2 = new EdgeCasePipeline(99, EdgeCaseTargetFactory.Schema).Apply(r2, strategies, includeNonValidityPreserving: false);

        m1.ToJson().ShouldBe(m2.ToJson());
        r1.MutableNode.ToJsonString().ShouldBe(r2.MutableNode.ToJsonString());
    }

    // ── Targeting safety ──────────────────────────────────────────────────────

    [Fact]
    public void GivenStringStrategies_WhenApplied_ThenCodeAndSystemFieldsUntouched()
    {
        var strategies = EdgeCaseCatalog.CreateDefault().Resolve(["string"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        new EdgeCasePipeline(7, EdgeCaseTargetFactory.Schema).Apply(resource, strategies, includeNonValidityPreserving: true);

        // gender (bound code), identifier.system / coding.system (uri) and coding.code (code) are
        // not free-text strings, so string strategies must leave them untouched. identifier.value
        // IS a string and is legitimately mutable, so it is not asserted here.
        resource.MutableNode["gender"]?.GetValue<string>().ShouldBe("male");
        var identifier = resource.MutableNode["identifier"]?.AsArray()?[0]?.AsObject();
        identifier?["system"]?.GetValue<string>().ShouldBe("http://example.org/mrn");
        var coding = identifier?["type"]?.AsObject()?["coding"]?.AsArray()?[0]?.AsObject();
        coding?["code"]?.GetValue<string>().ShouldBe("MR");
        coding?["system"]?.GetValue<string>().ShouldBe("http://hl7.org/fhir/v2/0203");
    }

    [Fact]
    public void GivenStringStrategies_WhenApplied_ThenIdentifierValueIsMutatedAndRecorded()
    {
        var strategies = EdgeCaseCatalog.CreateDefault().Resolve(["string"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        var manifest = new EdgeCasePipeline(7, EdgeCaseTargetFactory.Schema)
            .Apply(resource, strategies, includeNonValidityPreserving: true);

        // identifier.value is a FHIR string (not a code/uri), so it is legitimately mutable. The
        // refactor away from an element-name allowlist makes it reachable; assert that positively.
        var mutatedValue = resource.MutableNode["identifier"]?.AsArray()?[0]?.AsObject()?["value"]?.GetValue<string>();
        mutatedValue.ShouldNotBe("MRN-999");
        manifest.Mutations.ShouldContain(m => m.Path == "Patient.identifier[0].value");
    }

    // ── Determinism ───────────────────────────────────────────────────────────

    [Fact]
    public void GivenSameSeedAndInput_WhenIncludeInvalidAppliedTwice_ThenIdenticalOutput()
    {
        var strategies = EdgeCaseCatalog.CreateDefault().Resolve(["string"]);

        var r1 = ResourceJsonNode.Parse(SampleJson);
        var m1 = new EdgeCasePipeline(1234, EdgeCaseTargetFactory.Schema).Apply(r1, strategies, includeNonValidityPreserving: true);

        var r2 = ResourceJsonNode.Parse(SampleJson);
        var m2 = new EdgeCasePipeline(1234, EdgeCaseTargetFactory.Schema).Apply(r2, strategies, includeNonValidityPreserving: true);

        r1.MutableNode.ToJsonString().ShouldBe(r2.MutableNode.ToJsonString());
        m1.ToJson().ShouldBe(m2.ToJson());
        m1.Mutations.Count.ShouldBeGreaterThan(0);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static IEdgeCaseStrategy ResolveExact(string category)
    {
        var catalog = EdgeCaseCatalog.CreateDefault();
        var resolved = catalog.Resolve([category]);
        resolved.Count.ShouldBe(1, $"Expected exactly one strategy for '{category}'");
        return resolved[0];
    }
}
