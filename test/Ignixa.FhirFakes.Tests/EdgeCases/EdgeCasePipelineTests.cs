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
        var firstManifest = new EdgeCasePipeline(4242).Apply(first, strategies);

        var second = ResourceJsonNode.Parse(SampleJson);
        var secondManifest = new EdgeCasePipeline(4242).Apply(second, strategies);

        first.MutableNode.ToJsonString().ShouldBe(second.MutableNode.ToJsonString());
        firstManifest.ToJson().ShouldBe(secondManifest.ToJson());
    }

    [Fact]
    public void GivenUnicodeStrategies_WhenApplied_ThenBoundCodeAndSystemUrlUnchanged()
    {
        var unicode = EdgeCaseCatalog.CreateDefault().Resolve(["unicode"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        new EdgeCasePipeline(7).Apply(resource, unicode);

        resource.MutableNode["gender"]?.GetValue<string>().ShouldBe("male");
        var identifier = resource.MutableNode["identifier"]?.AsArray()?[0]?.AsObject();
        identifier?["system"]?.GetValue<string>().ShouldBe("http://hospital.example/mrn");
        identifier?["value"]?.GetValue<string>().ShouldBe("MRN-001");
    }

    [Fact]
    public void GivenUnicodeStrategies_WhenApplied_ThenFreeTextFamilyIsMutated()
    {
        var unicode = EdgeCaseCatalog.CreateDefault().Resolve(["unicode"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        var manifest = new EdgeCasePipeline(7).Apply(resource, unicode);

        var family = resource.MutableNode["name"]?.AsArray()?[0]?.AsObject()?["family"]?.GetValue<string>();
        family.ShouldNotBe("Smith");
        manifest.Mutations.ShouldContain(m => m.Path == "name[0].family");
    }

    [Fact]
    public void GivenTemporalStrategies_WhenApplied_ThenOnlyDateShapedValuesChangeAndStayValid()
    {
        var temporal = EdgeCaseCatalog.CreateDefault().Resolve(["temporal"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        var manifest = new EdgeCasePipeline(99).Apply(resource, temporal);

        var birthDate = resource.MutableNode["birthDate"]?.GetValue<string>();
        FhirDateRegex().IsMatch(birthDate!).ShouldBeTrue();

        resource.MutableNode["name"]?.AsArray()?[0]?.AsObject()?["family"]?.GetValue<string>().ShouldBe("Smith");
        manifest.Mutations.ShouldAllBe(m => m.Path == "birthDate");
    }

    [Fact]
    public void GivenMayViolateStrategy_WhenApplied_ThenItIsFilteredOut()
    {
        var resource = ResourceJsonNode.Parse(SampleJson);
        var strategies = new IEdgeCaseStrategy[] { new AlwaysFiresMayViolateStrategy() };

        var manifest = new EdgeCasePipeline(1).Apply(resource, strategies);

        manifest.Mutations.ShouldBeEmpty();
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
