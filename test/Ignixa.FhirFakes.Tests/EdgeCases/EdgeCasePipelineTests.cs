// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.FhirFakes.EdgeCases.Strategies;
using Ignixa.Serialization.SourceNodes;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.EdgeCases;

public partial class EdgeCasePipelineTests
{
    [GeneratedRegex(@"^\d{4}(-\d{2}(-\d{2}(T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?)?)?)?$")]
    private static partial Regex FhirDateRegex();

    private const string SampleJson = """
        {
          "resourceType": "Patient",
          "id": "abc-123",
          "gender": "male",
          "birthDate": "1990-03-15",
          "name": [
            { "family": "Smith", "given": ["John"] }
          ],
          "identifier": [
            { "system": "http://hospital.example/mrn", "value": "MRN-001" }
          ]
        }
        """;

    [Fact]
    public void GivenSameSeedAndInput_WhenAppliedTwice_ThenProducesIdenticalJsonAndManifest()
    {
        var strategies = EdgeCaseCatalog.CreateDefault().All();

        var first = ResourceJsonNode.Parse(SampleJson);
        var firstManifest = new EdgeCasePipeline(4242, EdgeCaseTargetFactory.Schema).Apply(first, strategies);

        var second = ResourceJsonNode.Parse(SampleJson);
        var secondManifest = new EdgeCasePipeline(4242, EdgeCaseTargetFactory.Schema).Apply(second, strategies);

        first.MutableNode.ToJsonString().ShouldBe(second.MutableNode.ToJsonString());
        firstManifest.ToJson().ShouldBe(secondManifest.ToJson());
        firstManifest.Mutations.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void GivenUnicodeStrategies_WhenApplied_ThenBoundCodeAndUriUnchanged()
    {
        var unicode = EdgeCaseCatalog.CreateDefault().Resolve(["unicode"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        new EdgeCasePipeline(7, EdgeCaseTargetFactory.Schema).Apply(resource, unicode);

        // gender is a bound code and system is a uri: neither is free-text, so both are off-limits.
        resource.MutableNode["gender"]?.GetValue<string>().ShouldBe("male");
        var identifier = resource.MutableNode["identifier"]?.AsArray()?[0]?.AsObject();
        identifier?["system"]?.GetValue<string>().ShouldBe("http://hospital.example/mrn");
    }

    [Fact]
    public void GivenUnicodeStrategies_WhenApplied_ThenFreeTextFamilyIsMutated()
    {
        var unicode = EdgeCaseCatalog.CreateDefault().Resolve(["unicode"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        var manifest = new EdgeCasePipeline(7, EdgeCaseTargetFactory.Schema).Apply(resource, unicode);

        var family = resource.MutableNode["name"]?.AsArray()?[0]?.AsObject()?["family"]?.GetValue<string>();
        family.ShouldNotBe("Smith");
        manifest.Mutations.ShouldContain(m => m.Path == "Patient.name[0].family");
    }

    [Fact]
    public void GivenTemporalStrategies_WhenApplied_ThenOnlyDateShapedValuesChangeAndStayValid()
    {
        var temporal = EdgeCaseCatalog.CreateDefault().Resolve(["temporal"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        var manifest = new EdgeCasePipeline(99, EdgeCaseTargetFactory.Schema).Apply(resource, temporal);

        var birthDate = resource.MutableNode["birthDate"]?.GetValue<string>();
        FhirDateRegex().IsMatch(birthDate!).ShouldBeTrue();

        resource.MutableNode["name"]?.AsArray()?[0]?.AsObject()?["family"]?.GetValue<string>().ShouldBe("Smith");
        manifest.Mutations.ShouldAllBe(m => m.Path == "Patient.birthDate");
    }

    [Fact]
    public void GivenMayViolateStrategy_WhenApplied_ThenItIsFilteredOut()
    {
        var resource = ResourceJsonNode.Parse(SampleJson);
        var strategies = new IEdgeCaseStrategy[] { new AlwaysFiresMayViolateStrategy() };

        var manifest = new EdgeCasePipeline(1, EdgeCaseTargetFactory.Schema).Apply(resource, strategies);

        manifest.Mutations.ShouldBeEmpty();
    }

    [Fact]
    public void GivenFixedInputAndSeed_WhenApplied_ThenProducesKnownGoldenOutput()
    {
        const string goldenJson = """
            {
              "resourceType": "Patient",
              "id": "golden-001",
              "gender": "male",
              "birthDate": "1990-03-15",
              "name": [
                { "family": "Smith", "given": ["John"] }
              ]
            }
            """;
        var strategies = EdgeCaseCatalog.CreateDefault().Resolve(["temporal", "unicode"]);
        var resource = ResourceJsonNode.Parse(goldenJson);

        var manifest = new EdgeCasePipeline(7777, EdgeCaseTargetFactory.Schema).Apply(resource, strategies);

        manifest.Mutations.Count.ShouldBe(3);
        manifest.Mutations[0].Category.ShouldBe("temporal.far-future");
        manifest.Mutations[0].Path.ShouldBe("Patient.birthDate");
        manifest.Mutations[0].After.ShouldBe("2999-06-15");
        manifest.Mutations[1].Path.ShouldBe("Patient.name[0].family");
        manifest.Mutations[1].Category.ShouldBe("unicode.zero-width");
        manifest.Mutations[2].Path.ShouldBe("Patient.name[0].given[0]");
        manifest.Mutations[2].Category.ShouldBe("unicode.cjk");
    }

    private sealed class AlwaysFiresMayViolateStrategy : IEdgeCaseStrategy
    {
        public string Category => "test.may-violate";

        public EdgeCaseFamily Family => EdgeCaseFamily.Structural;

        public ValidityIntent Intent => ValidityIntent.MayViolate;

        public bool CanApply(MutationTarget target) => true;

        public MutationResult Apply(MutationTarget target, Bogus.Randomizer rng)
            => new("MUTATED", "should never run in default mode");
    }
}
