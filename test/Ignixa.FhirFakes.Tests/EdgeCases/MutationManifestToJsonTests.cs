// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.Serialization.SourceNodes;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.EdgeCases;

public class MutationManifestToJsonTests
{
    private const string SampleJson = """
        {
          "resourceType": "Patient",
          "id": "manifest-test",
          "name": [{ "family": "Smith", "given": ["John"] }],
          "birthDate": "1990-01-01"
        }
        """;

    [Fact]
    public void GivenManifestWithMutations_WhenSerializedToJson_ThenContainsResourceIdSeedAndMutations()
    {
        var strategies = EdgeCaseCatalog.CreateDefault().Resolve(["unicode"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        var manifest = new EdgeCasePipeline(42, EdgeCaseTargetFactory.Schema).Apply(resource, strategies);
        var json = JsonNode.Parse(manifest.ToJson());

        json!["resourceId"].ShouldNotBeNull();
        json["seed"]?.GetValue<int>().ShouldBe(42);
        json["mutations"].ShouldNotBeNull();
        json["mutations"].ShouldBeOfType<JsonArray>();
    }

    [Fact]
    public void GivenManifestWithMutation_WhenSerializedToJson_ThenEachMutationHasAllRequiredFields()
    {
        var strategies = EdgeCaseCatalog.CreateDefault().Resolve(["unicode"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        var manifest = new EdgeCasePipeline(7, EdgeCaseTargetFactory.Schema).Apply(resource, strategies);
        var json = JsonNode.Parse(manifest.ToJson());
        var mutations = json!["mutations"]?.AsArray();

        mutations.ShouldNotBeNull();
        mutations!.Count.ShouldBeGreaterThan(0);

        var firstMutation = mutations[0];
        firstMutation?["category"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
        firstMutation?["path"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
        firstMutation?["before"]?.GetValue<string>().ShouldNotBeNull();
        firstMutation?["after"]?.GetValue<string>().ShouldNotBeNull();
        firstMutation?["description"]?.GetValue<string>().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void GivenManifestWithNoMutations_WhenSerializedToJson_ThenMutationsArrayIsEmpty()
    {
        var manifest = new MutationManifest("res-1", 999, []);

        var json = JsonNode.Parse(manifest.ToJson());

        json!["resourceId"]?.GetValue<string>().ShouldBe("res-1");
        json["seed"]?.GetValue<int>().ShouldBe(999);
        var mutations = json["mutations"]?.AsArray();
        mutations.ShouldNotBeNull();
        mutations!.Count.ShouldBe(0);
    }

    [Fact]
    public void GivenManifestJson_WhenParsedBack_ThenSeedAndResourceIdRoundTrip()
    {
        var records = new List<MutationRecord>
        {
            new MutationRecord("unicode.rtl", "name[0].family", "Smith", "محمد", "Replaced with RTL"),
        };
        var manifest = new MutationManifest("test-patient", 7777, records);

        var json = JsonNode.Parse(manifest.ToJson());

        json!["resourceId"]!.GetValue<string>().ShouldBe("test-patient");
        json["seed"]!.GetValue<int>().ShouldBe(7777);
        var description = json["mutations"]![0]!["description"]!.GetValue<string>();
        description.ShouldNotBeNullOrEmpty();
        description.ShouldBe("Replaced with RTL");
    }
}
