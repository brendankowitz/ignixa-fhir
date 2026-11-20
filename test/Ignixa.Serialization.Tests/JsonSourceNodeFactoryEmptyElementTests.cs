// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Serialization.Tests;

/// <summary>
/// FHIR JSON objects and arrays are never empty and properties are never null
/// (https://hl7.org/fhir/json.html, invariant ele-1), so serialization omits them.
/// </summary>
public class JsonSourceNodeFactoryEmptyElementTests
{
    private static readonly JsonSerializerOptions ConverterOptions = new() { Converters = { new Models.JsonNodeConverterFactory() } };

    public static TheoryData<string> Serializers => SerializerOverloads.All;

    [Theory]
    [MemberData(nameof(Serializers))]
    public async Task GivenMetaMaterializedByAGetter_WhenItsOnlyValueIsCleared_ThenMetaIsOmitted(string serializer)
    {
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Patient","id":"a"}""");

        patient.Meta.VersionId = null;

        (await SerializerOverloads.SerializeAsync(patient, serializer)).ShouldBe("""{"resourceType":"Patient","id":"a"}""");
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public async Task GivenNestedEmptyObjectsAndArrays_WhenSerializing_ThenEmptiesAndTheirEmptiedParentsAreOmitted(string serializer)
    {
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            """
            {
              "resourceType": "Patient",
              "id": "a",
              "meta": { "tag": [ {}, { "extension": [] } ] },
              "name": [ {}, { "family": "x", "given": [], "period": {} } ],
              "contact": [ { "name": { "extension": [ {} ] } } ],
              "telecom": []
            }
            """);

        (await SerializerOverloads.SerializeAsync(patient, serializer)).ShouldBe(
            """{"resourceType":"Patient","id":"a","name":[{"family":"x"}]}""");
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public async Task GivenATopLevelPrimitiveArrayWithACompanion_WhenSerializing_ThenPlaceholdersAlignAndDoublyEmptyPositionsAreDropped(string serializer)
    {
        var questionnaire = JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            """{"resourceType":"Questionnaire","subjectType":["Patient",null,null,"Group"],"_subjectType":[null,{},{"id":"x"},null]}""");

        (await SerializerOverloads.SerializeAsync(questionnaire, serializer)).ShouldBe(
            """{"resourceType":"Questionnaire","subjectType":["Patient",null,"Group"],"_subjectType":[null,{"id":"x"},null]}""");
    }

    [Fact]
    public void GivenNullPropertyValues_WhenSerializing_ThenTheyAreOmitted()
    {
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            """{"resourceType":"Patient","id":"a","gender":null,"name":[{"family":null,"given":["x"]}]}""");

        patient.SerializeToString().ShouldBe("""{"resourceType":"Patient","id":"a","name":[{"given":["x"]}]}""");
    }

    [Fact]
    public void GivenAnEmptyPrimitiveCompanion_WhenSerializing_ThenOnlyTheValueIsWritten()
    {
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            """{"resourceType":"Patient","birthDate":"1970-01-01","_birthDate":{"extension":[{}]}}""");

        patient.SerializeToString().ShouldBe("""{"resourceType":"Patient","birthDate":"1970-01-01"}""");
    }

    [Fact]
    public void GivenAPrimitiveArrayWithACompanion_WhenSerializing_ThenNullPlaceholdersKeepTheArraysAligned()
    {
        const string json =
            """{"resourceType":"Patient","name":[{"given":["a",null,"c"],"_given":[null,{"id":"b"},null]}]}""";

        JsonSourceNodeFactory.Parse<ResourceJsonNode>(json).SerializeToString().ShouldBe(json);
    }

    [Fact]
    public void GivenAnEmptyItemInAPrimitiveCompanionArray_WhenSerializing_ThenItBecomesANullPlaceholder()
    {
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            """{"resourceType":"Patient","name":[{"given":["a","b"],"_given":[{"extension":[]},{"id":"b"}]}]}""");

        patient.SerializeToString().ShouldBe(
            """{"resourceType":"Patient","name":[{"given":["a","b"],"_given":[null,{"id":"b"}]}]}""");
    }

    [Fact]
    public void GivenAPositionEmptyInBothCompanionArrays_WhenSerializing_ThenItIsDroppedFromBoth()
    {
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            """{"resourceType":"Patient","name":[{"given":[null,"b","c"],"_given":[{},{"id":"x"},null]}]}""");

        patient.SerializeToString().ShouldBe(
            """{"resourceType":"Patient","name":[{"given":["b","c"],"_given":[{"id":"x"},null]}]}""");
    }

    [Fact]
    public void GivenACompanionArrayThatIsEntirelyEmpty_WhenSerializing_ThenTheCompanionAndPlaceholdersAreDropped()
    {
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            """{"resourceType":"Patient","name":[{"given":["a",null],"_given":[null,{}]}]}""");

        patient.SerializeToString().ShouldBe("""{"resourceType":"Patient","name":[{"given":["a"]}]}""");
    }

    [Fact]
    public void GivenACompanionArrayWithoutValues_WhenSerializing_ThenOnlyTheCompanionIsWritten()
    {
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            """{"resourceType":"Patient","name":[{"given":[null],"_given":[{"id":"b"}]}]}""");

        patient.SerializeToString().ShouldBe("""{"resourceType":"Patient","name":[{"_given":[{"id":"b"}]}]}""");
    }

    [Fact]
    public void GivenAnEmptyContainedResourceAndBundleEntry_WhenSerializing_ThenTheyAreOmitted()
    {
        var bundle = JsonSourceNodeFactory.Parse<ResourceJsonNode>(
            """{"resourceType":"Bundle","type":"collection","entry":[{"resource":null},{"fullUrl":"urn:x","resource":{"resourceType":"Patient","meta":{}}}]}""");

        bundle.SerializeToString().ShouldBe(
            """{"resourceType":"Bundle","type":"collection","entry":[{"fullUrl":"urn:x","resource":{"resourceType":"Patient"}}]}""");
    }

    [Fact]
    public void GivenEmptyElements_WhenSerializingThroughJsonSerializer_ThenTheConverterOmitsThem()
    {
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Patient","id":"a","meta":{"tag":[]}}""");

        JsonSerializer.Serialize(patient, ConverterOptions).ShouldBe("""{"resourceType":"Patient","id":"a"}""");
    }

    [Fact]
    public void GivenEmptyElements_WhenSerializing_ThenTheResourceItselfIsNotModified()
    {
        var patient = JsonSourceNodeFactory.Parse<ResourceJsonNode>("""{"resourceType":"Patient","meta":{}}""");

        patient.SerializeToString();

        patient.MutableNode.ContainsKey("meta").ShouldBeTrue();
    }

    [Fact]
    public void GivenATreeDeeperThanTheWriterAllows_WhenSerializing_ThenItThrowsInsteadOfOverflowingTheStack()
    {
        var root = new JsonObject { ["resourceType"] = "Basic" };
        JsonObject current = root;
        for (int i = 0; i < 20_000; i++)
        {
            var child = new JsonObject();
            current["extension"] = child;
            current = child;
        }

        current["url"] = "leaf";

        Should.Throw<InvalidOperationException>(() => new ResourceJsonNode(root).SerializeToString());
    }
}
