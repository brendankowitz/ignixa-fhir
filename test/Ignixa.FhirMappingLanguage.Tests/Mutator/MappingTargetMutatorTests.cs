// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Mutator;

public class MappingTargetMutatorTests
{
    private readonly ISchema _schema = FhirVersion.R4.GetSchemaProvider();
    private readonly IMappingTargetMutator _mutator;

    public MappingTargetMutatorTests()
    {
        _mutator = new JsonNodeMutator(new FhirPathEvaluator(), new FhirPathParser(), () => _schema);
    }

    [Theory]
    [InlineData("HumanName")]
    [InlineData("Quantity")]
    [InlineData("Coding")]
    [InlineData("Bundle.entry")]
    public void GivenDatatype_WhenCreatedWithoutValue_ThenCreatesEmptySchemaAwareObject(string typeName)
    {
        var result = _mutator.CreateElement(typeName);

        result.InstanceType.ShouldBe(typeName);
        result.Type.ShouldBeSameAs(_schema.GetTypeDefinition(typeName));
        result.Meta<JsonNode>()!.ToJsonString().ShouldBe("{}");
    }

    [Fact]
    public void GivenResource_WhenCreatedWithoutValue_ThenOnlyResourceTypeIsPresent()
    {
        var result = _mutator.CreateElement("Patient");

        result.InstanceType.ShouldBe("Patient");
        result.Meta<JsonNode>()!.ToJsonString().ShouldBe("""{"resourceType":"Patient"}""");
    }

    [Fact]
    public void GivenCodeableConceptDictionary_WhenCreated_ThenPreservesCodingArrayAndSchema()
    {
        var input = new Dictionary<string, object?>
        {
            ["coding"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["system"] = "http://loinc.org",
                    ["code"] = "1234-5"
                }
            },
            ["text"] = "Example"
        };

        var result = _mutator.CreateElement("CodeableConcept", input);

        result.Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"coding":[{"system":"http://loinc.org","code":"1234-5"}],"text":"Example"}""");
        var coding = result.Children("coding").ShouldHaveSingleItem();
        coding.InstanceType.ShouldBe("Coding");
        coding.Type!.IsCollection.ShouldBeTrue();
        coding.Children("code").ShouldHaveSingleItem().Value.ShouldBe("1234-5");
    }

    [Fact]
    public void GivenCreatedTargetAlias_WhenMutatedAfterNavigation_ThenRefreshObservesLiveTarget()
    {
        var patient = _mutator.CreateElement("Patient");
        patient.Children("name").ShouldBeEmpty();
        var name = _mutator.SetTarget(patient, "name", null);
        name.Children("family").ShouldBeEmpty();

        _mutator.SetTarget(name, "family", _mutator.CreateElement("string", "Target"));

        name.Meta<JsonNode>().ShouldBeSameAs(patient.Meta<JsonNode>()!["name"]![0]);
        _mutator.Refresh(name).Children("family").ShouldHaveSingleItem().Value.ShouldBe("Target");
        _mutator.Refresh(patient).Children("name").ShouldHaveSingleItem()
            .Children("family").ShouldHaveSingleItem().Value.ShouldBe("Target");
    }

    [Fact]
    public void GivenSourceElement_WhenCopiedToTargetAndMutated_ThenSourceIsUnchanged()
    {
        var sourceJson = JsonNode.Parse("""{"family":"Source","given":["Original"]}""")!;
        var source = _mutator.CreateElement("HumanName", sourceJson);
        var patient = _mutator.CreateElement("Patient");

        var target = _mutator.SetTarget(patient, "name", source);
        _mutator.SetTarget(target, "family", _mutator.CreateElement("string", "Target"));
        _mutator.SetTarget(target, "given", _mutator.CreateElement("string", "Added"));

        target.Meta<JsonNode>().ShouldBeSameAs(patient.Meta<JsonNode>()!["name"]![0]);
        sourceJson.ToJsonString().ShouldBe("""{"family":"Source","given":["Original"]}""");
        source.Meta<JsonNode>()!.ToJsonString().ShouldBe(sourceJson.ToJsonString());
        target.Meta<JsonNode>()!.ToJsonString().ShouldBe("""{"family":"Target","given":["Original","Added"]}""");
    }

    [Fact]
    public void GivenCollectionTarget_WhenAppendedAndInsertedFirst_ThenAliasesAndOrderArePreserved()
    {
        var patient = _mutator.CreateElement("Patient");
        var first = _mutator.SetTarget(patient, "name", null);
        var second = _mutator.SetTarget(patient, "name", null);
        var inserted = _mutator.SetTarget(patient, "name", null, insertFirst: true);

        _mutator.SetTarget(first, "family", _mutator.CreateElement("string", "First"));
        _mutator.SetTarget(second, "family", _mutator.CreateElement("string", "Second"));
        _mutator.SetTarget(inserted, "family", _mutator.CreateElement("string", "Inserted"));

        patient.Meta<JsonNode>()!["name"]!.ToJsonString().ShouldBe(
            """[{"family":"Inserted"},{"family":"First"},{"family":"Second"}]""");
        _mutator.Refresh(first).Meta<JsonNode>().ShouldBeSameAs(patient.Meta<JsonNode>()!["name"]![1]);
    }

    [Fact]
    public void GivenCollectionTarget_WhenReplaced_ThenRetainsArrayShapeWithOnlyReplacement()
    {
        var name = _mutator.CreateElement("HumanName");
        _mutator.SetTarget(name, "given", _mutator.CreateElement("string", "Old"));
        _mutator.SetTarget(name, "given", _mutator.CreateElement("string", "Other"));

        var result = _mutator.SetTarget(name, "given", _mutator.CreateElement("string", "New"), PropertyMutationMode.Replace);

        name.Meta<JsonNode>()!.ToJsonString().ShouldBe("""{"given":["New"]}""");
        result.Meta<JsonNode>().ShouldBeSameAs(name.Meta<JsonNode>()!["given"]![0]);
        result.Type!.IsCollection.ShouldBeTrue();
    }

    [Fact]
    public void GivenScalarTarget_WhenAppendRequested_ThenStillSetsScalar()
    {
        var name = _mutator.CreateElement("HumanName");
        _mutator.SetTarget(name, "family", _mutator.CreateElement("string", "Old"));

        _mutator.SetTarget(name, "family", _mutator.CreateElement("string", "New"), PropertyMutationMode.Append);

        name.Meta<JsonNode>()!.ToJsonString().ShouldBe("""{"family":"New"}""");
    }

    [Fact]
    public void GivenChoiceTarget_WhenQuantityAssigned_ThenUsesChoiceSuffixAndLiveQuantity()
    {
        var observation = _mutator.CreateElement("Observation");
        var quantity = _mutator.CreateElement("Quantity", new Dictionary<string, object?> { ["value"] = 4m });

        var result = _mutator.SetTarget(observation, "value", quantity);
        _mutator.SetTarget(result, "unit", _mutator.CreateElement("string", "mg"));

        result.InstanceType.ShouldBe("Quantity");
        result.Meta<JsonNode>().ShouldBeSameAs(observation.Meta<JsonNode>()!["valueQuantity"]);
        observation.Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"resourceType":"Observation","valueQuantity":{"value":4,"unit":"mg"}}""");
        quantity.Meta<JsonNode>()!.ToJsonString().ShouldBe("""{"value":4}""");
    }

    [Fact]
    public void GivenExistingChoice_WhenDifferentTypeAssigned_ThenRemovesOldVariantAndMetadata()
    {
        var observation = _mutator.CreateElement("Observation", JsonNode.Parse(
            """{"valueString":"old","_valueString":{"id":"old-metadata"}}"""));

        _mutator.SetTarget(observation, "value[x]", _mutator.CreateElement("Quantity"));

        observation.Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"resourceType":"Observation","valueQuantity":{}}""");
        _mutator.Refresh(observation).Children("value").ShouldHaveSingleItem().InstanceType.ShouldBe("Quantity");
    }

    [Fact]
    public void GivenExplicitChoiceWireName_WhenValueOmitted_ThenCreatesSpecifiedType()
    {
        var observation = _mutator.CreateElement("Observation");

        var result = _mutator.SetTarget(observation, "valueQuantity", null);
        _mutator.SetTarget(result, "value", _mutator.CreateElement("decimal", 2.5m));

        result.InstanceType.ShouldBe("Quantity");
        observation.Meta<JsonNode>()!["valueQuantity"]!.ToJsonString().ShouldBe("""{"value":2.5}""");
    }

    [Fact]
    public void GivenBundleEntry_WhenNestedResourceAndBackboneAreCreated_ThenAllAliasesRemainLive()
    {
        var bundle = _mutator.CreateElement("Bundle");
        var entry = _mutator.SetTarget(bundle, "entry", null);
        var observation = _mutator.SetTarget(entry, "resource", _mutator.CreateElement("Observation"));
        var component = _mutator.SetTarget(observation, "component", null);
        var code = _mutator.SetTarget(component, "code", null);

        _mutator.SetTarget(code, "text", _mutator.CreateElement("string", "Component"));
        _mutator.SetTarget(component, "value", _mutator.CreateElement("string", "Result"));

        entry.InstanceType.ShouldBe("Bundle.Entry");
        component.InstanceType.ShouldBe("Observation.Component");
        bundle.Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"resourceType":"Bundle","entry":[{"resource":{"resourceType":"Observation","component":[{"code":{"text":"Component"},"valueString":"Result"}]}}]}""");
    }

    [Fact]
    public void GivenPrimitiveWithShadow_WhenCopiedAndRefreshed_ThenValueAndMetadataArePreserved()
    {
        var source = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","birthDate":"1980-01-01","_birthDate":{"id":"source-date","extension":[{"url":"http://example.org/note","valueString":"metadata"}]}}""");
        var birthDate = source.ToElement(_schema).Children("birthDate").ShouldHaveSingleItem();
        var target = _mutator.CreateElement("Patient");

        var copied = _mutator.SetTarget(target, "birthDate", birthDate);
        var refreshed = _mutator.Refresh(copied);
        _mutator.SetTarget(refreshed, "id", _mutator.CreateElement("string", "target-date"));

        target.Meta<JsonNode>()!["birthDate"]!.GetValue<string>().ShouldBe("1980-01-01");
        target.Meta<JsonNode>()!["_birthDate"]!["id"]!.GetValue<string>().ShouldBe("target-date");
        target.Meta<JsonNode>()!["_birthDate"]!["extension"]![0]!["valueString"]!.GetValue<string>().ShouldBe("metadata");
        refreshed.Meta<JsonPrimitiveValueNode>()!.Value.ShouldBeSameAs(target.Meta<JsonNode>()!["birthDate"]);
        birthDate.Children("id").ShouldHaveSingleItem().Value.ShouldBe("source-date");
    }

    [Fact]
    public void GivenPrimitiveCollectionWithMetadata_WhenInsertedFirst_ThenShadowsStayAligned()
    {
        var source = _mutator.CreateElement("HumanName", JsonNode.Parse(
            """{"given":["Source"],"_given":[{"id":"metadata"}]}"""));
        var target = _mutator.CreateElement("HumanName");
        _mutator.SetTarget(target, "given", _mutator.CreateElement("string", "Existing"));

        var inserted = _mutator.SetTarget(target, "given", source.Children("given")[0], insertFirst: true);
        _mutator.SetTarget(target, "given", _mutator.CreateElement("string", "Last"));

        target.Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"given":["Source","Existing","Last"],"_given":[{"id":"metadata"},null,null]}""");
        _mutator.Refresh(inserted).Value.ShouldBe("Source");
        _mutator.Refresh(inserted).Children("id").ShouldHaveSingleItem().Value.ShouldBe("metadata");
    }

    [Fact]
    public void GivenPrimitiveAliasWithoutShadow_WhenMetadataAdded_ThenCreatesAlignedShadowWithoutChangingValue()
    {
        var name = _mutator.CreateElement("HumanName");
        var first = _mutator.SetTarget(name, "given", _mutator.CreateElement("string", "First"));
        _mutator.SetTarget(name, "given", _mutator.CreateElement("string", "Second"));

        _mutator.SetTarget(first, "id", _mutator.CreateElement("string", "new-metadata"));

        name.Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"given":["First","Second"],"_given":[{"id":"new-metadata"},null]}""");
        _mutator.Refresh(first).Value.ShouldBe("First");
        _mutator.Refresh(first).Children("id").ShouldHaveSingleItem().Value.ShouldBe("new-metadata");
    }

    [Theory]
    [InlineData("NoSuchType")]
    [InlineData("Resource")]
    public void GivenUnknownOrAbstractType_WhenCreated_ThenFailsExplicitly(string typeName)
    {
        Should.Throw<InvalidOperationException>(() => _mutator.CreateElement(typeName))
            .Message.ShouldContain(typeName);
    }

    [Theory]
    [InlineData("missingChild")]
    [InlineData("value")]
    [InlineData("valueNoSuchType")]
    public void GivenUnknownOrAmbiguousTarget_WhenCreated_ThenFailsWithoutChangingParent(string propertyName)
    {
        var observation = _mutator.CreateElement("Observation");

        Should.Throw<InvalidOperationException>(() => _mutator.SetTarget(observation, propertyName, null));

        observation.Meta<JsonNode>()!.ToJsonString().ShouldBe("""{"resourceType":"Observation"}""");
    }

    [Fact]
    public void GivenMismatchedChoiceType_WhenAssigned_ThenFailsWithoutChangingParent()
    {
        var observation = _mutator.CreateElement("Observation");

        Should.Throw<InvalidOperationException>(() =>
            _mutator.SetTarget(observation, "valueQuantity", _mutator.CreateElement("string", "wrong")));

        observation.Meta<JsonNode>()!.ToJsonString().ShouldBe("""{"resourceType":"Observation"}""");
    }

    [Fact]
    public void GivenUnsupportedClrObject_WhenCreated_ThenDoesNotSerializeArbitraryShape()
    {
        Should.Throw<InvalidOperationException>(() => _mutator.CreateElement("Coding", new object()));
    }
}
