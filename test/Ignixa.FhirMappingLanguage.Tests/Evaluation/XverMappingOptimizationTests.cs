/*
 * Copyright (c) 2026, Ignixa Contributors
 */

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class XverMappingOptimizationTests
{
    [Fact]
    public void GivenComplexCollectionAliases_WhenAnItemIsMoved_ThenRefreshedAliasesHaveCurrentIndices()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var patient = mutator.CreateElement("Patient");
        var first = mutator.SetTarget(patient, "name", mutator.CreateElement("HumanName",
            new Dictionary<string, object?> { ["family"] = "First" }));
        var second = mutator.SetTarget(patient, "name", mutator.CreateElement("HumanName",
            new Dictionary<string, object?> { ["family"] = "Second" }));

        mutator.MoveTarget(second, 0);

        var movedSecond = mutator.Refresh(second);
        var movedFirst = mutator.Refresh(first);
        movedSecond.Location.ShouldBe("Patient.name[0]");
        movedSecond.Children("family").ShouldHaveSingleItem().Value.ShouldBe("Second");
        movedFirst.Location.ShouldBe("Patient.name[1]");
        movedFirst.Children("family").ShouldHaveSingleItem().Value.ShouldBe("First");
        patient.Meta<JsonNode>()!["name"]!.ToJsonString().ShouldBe(
            """[{"family":"Second"},{"family":"First"}]""");
    }

    [Fact]
    public void GivenPrimitiveCollectionWithShadows_WhenAnItemIsMoved_ThenValuesAndShadowsStayIndexedTogether()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var source = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","name":[{"given":["First","Second"],"_given":[null,{"id":"second-id"}]}]}""");
        var patient = mutator.CreateElement("Patient");
        var name = mutator.SetTarget(patient, "name", source.ToElement(schema).Children("name").ShouldHaveSingleItem());
        var given = name.Children("given");
        var first = given[0];
        var second = given[1];

        mutator.MoveTarget(second, 0);

        var refreshedName = mutator.Refresh(name);
        var refreshedGiven = refreshedName.Children("given");
        refreshedGiven.Count.ShouldBe(2);
        refreshedGiven[0].Location.ShouldBe("Patient.name[0].given[0]");
        refreshedGiven[0].Value.ShouldBe("Second");
        refreshedGiven[0].Children("id").ShouldHaveSingleItem().Value.ShouldBe("second-id");
        refreshedGiven[1].Location.ShouldBe("Patient.name[0].given[1]");
        refreshedGiven[1].Value.ShouldBe("First");
        patient.Meta<JsonNode>()!["name"]![0]!.ToJsonString().ShouldBe(
            """{"given":["Second","First"],"_given":[{"id":"second-id"},null]}""");
        mutator.Refresh(first).Location.ShouldBe(".given[1]");
    }

    [Fact]
    public void GivenMisalignedPrimitiveAndShadowArrays_WhenRefreshingShadowOnlyIndex_ThenKeepsItsMetadataAndLocation()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var source = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","name":[{"given":["First"],"_given":[null,{"id":"shadow-only"}]}]}""");
        var shadowOnly = source.ToElement(schema).Children("name").ShouldHaveSingleItem()
            .Children("given")[1];

        var refreshed = mutator.Refresh(shadowOnly);

        refreshed.Location.ShouldBe(".given[1]");
        refreshed.HasPrimitiveValue.ShouldBeFalse();
        refreshed.Value.ShouldBeNull();
        refreshed.Children("id").ShouldHaveSingleItem().Value.ShouldBe("shadow-only");
        refreshed.InstanceType.ShouldBe("string");
    }

    [Fact]
    public void GivenChoiceTarget_WhenWrapped_ThenUsesWireNameAndRetainsTypedChildren()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var observation = mutator.CreateElement("Observation");
        var quantity = mutator.CreateElement("Quantity", new Dictionary<string, object?> { ["value"] = 2.5m });

        var result = mutator.SetTarget(observation, "value", quantity);
        mutator.SetTarget(result, "unit", mutator.CreateElement("string", "mg"));

        var refreshed = mutator.Refresh(result);
        refreshed.InstanceType.ShouldBe("Quantity");
        refreshed.Location.ShouldBe("Observation.valueQuantity");
        refreshed.Children("value").ShouldHaveSingleItem().Value.ShouldBe(2.5m);
        refreshed.Children("unit").ShouldHaveSingleItem().Value.ShouldBe("mg");
        observation.Meta<JsonNode>()!["valueQuantity"]!.ToJsonString().ShouldBe(
            """{"value":2.5,"unit":"mg"}""");
    }

    [Fact]
    public void GivenImmutableSource_WhenMappingToJsonTarget_ThenCopiesItsValueWithoutRequiringRefresh()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var sourceId = new ImmutableElement("id", "string", "immutable-id");
        var source = new ImmutableElement("src", "Patient", children: [sourceId]);
        var target = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        var context = new MappingContext();
        context.SetSource("src", source);
        context.SetTarget("tgt", target.ToElement(schema));

        Execute(ParseMap("src.id -> tgt.id;"), context, mutator);

        target.ToSourceNavigator().Meta<JsonNode>()!["id"]!.GetValue<string>().ShouldBe("immutable-id");
        source.Children("id").ShouldHaveSingleItem().ShouldBeSameAs(sourceId);
        sourceId.Value.ShouldBe("immutable-id");
    }

    [Fact]
    public void GivenCrossVersionSourceAndReusedEvaluator_WhenExecutingAgainWithSameEditedSource_ThenCacheIsResetAndTargetViewIsPreserved()
    {
        var sourceSchema = FhirVersion.R4.GetSchemaProvider();
        var targetSchema = FhirVersion.Stu3.GetSchemaProvider();
        var mutator = new RefreshCountingMutator(CreateMutator(targetSchema));
        var evaluator = new MappingEvaluator(MappingEvaluatorOptions.Default, mutator);
        var map = ParseMap("src.name -> tgt.name; src.name -> tgt.name;");
        var source = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","name":[{"family":"First","given":["R4"]}]}""");
        var firstTarget = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        var context = CreateContext(source, firstTarget, sourceSchema, targetSchema);
        var sourceElement = context.GetSource("src")!;

        Execute(map, context, evaluator);

        mutator.RefreshCount(sourceElement).ShouldBe(1);
        firstTarget.ToSourceNavigator().Meta<JsonNode>()!["name"]![0]!["family"]!.GetValue<string>().ShouldBe("First");
        firstTarget.ToSourceNavigator().Meta<JsonNode>()!["name"]![0]!["given"]![0]!.GetValue<string>().ShouldBe("R4");

        sourceElement.Meta<JsonNode>()!["name"]![0]!["family"] = "Second";
        sourceElement.Meta<JsonNode>()!["name"]![0]!["given"]![0] = "Updated between executions";
        var secondTarget = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        context.SetTarget("tgt", secondTarget.ToElement(targetSchema));

        Execute(map, context, evaluator);

        mutator.RefreshCount(sourceElement).ShouldBe(2);
        secondTarget.ToSourceNavigator().Meta<JsonNode>()!["name"]![0]!["family"]!.GetValue<string>().ShouldBe("Second");
        secondTarget.ToSourceNavigator().Meta<JsonNode>()!["name"]![0]!["given"]![0]!.GetValue<string>()
            .ShouldBe("Updated between executions");
        firstTarget.ToSourceNavigator().Meta<JsonNode>()!["name"]![0]!["family"]!.GetValue<string>().ShouldBe("First");
        context.GetSource("src").ShouldBeSameAs(sourceElement);
    }
    [Fact]
    public void GivenR4CanonicalSourceAndStu3UriTarget_WhenReusingEvaluator_ThenFirstSourceRefreshPreservesSchemaInterpretation()
    {
        var sourceSchema = FhirVersion.R4.GetSchemaProvider();
        var targetSchema = FhirVersion.Stu3.GetSchemaProvider();
        var mutator = new RefreshCountingMutator(CreateMutator(targetSchema));
        var evaluator = new MappingEvaluator(MappingEvaluatorOptions.Default, mutator);
        var map = new MappingParser().Parse("""
            map 'http://example.org/xver-canonical-uri-guard' = 'CanonicalUriGuard'
            group Main(source src : CodeSystem, target tgt : CodeSystem) {
              src.valueSet -> tgt.valueSet;
            }
            """);
        var firstSource = ResourceJsonNode.Parse(
            """{"resourceType":"CodeSystem","valueSet":"http://example.org/ValueSet/first"}""");
        var firstTarget = ResourceJsonNode.Parse("""{"resourceType":"CodeSystem"}""");
        var firstContext = CreateContext(firstSource, firstTarget, sourceSchema, targetSchema);
        var firstSourceElement = firstContext.GetSource("src")!;
        firstSourceElement.Children("valueSet").ShouldHaveSingleItem().InstanceType.ShouldBe("canonical");

        Execute(map, firstContext, evaluator);

        firstTarget.ToSourceNavigator().Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"resourceType":"CodeSystem","valueSet":"http://example.org/ValueSet/first"}""");
        firstSource.ToSourceNavigator().Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"resourceType":"CodeSystem","valueSet":"http://example.org/ValueSet/first"}""");
        mutator.RefreshCount(firstSourceElement).ShouldBe(1);

        var secondSource = ResourceJsonNode.Parse(
            """{"resourceType":"CodeSystem","valueSet":"http://example.org/ValueSet/second"}""");
        var secondTarget = ResourceJsonNode.Parse("""{"resourceType":"CodeSystem"}""");
        var secondContext = CreateContext(secondSource, secondTarget, sourceSchema, targetSchema);
        var secondSourceElement = secondContext.GetSource("src")!;
        secondSourceElement.Children("valueSet").ShouldHaveSingleItem().InstanceType.ShouldBe("canonical");

        Execute(map, secondContext, evaluator);

        secondTarget.ToSourceNavigator().Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"resourceType":"CodeSystem","valueSet":"http://example.org/ValueSet/second"}""");
        secondSource.ToSourceNavigator().Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"resourceType":"CodeSystem","valueSet":"http://example.org/ValueSet/second"}""");
        mutator.RefreshCount(secondSourceElement).ShouldBe(1);
        firstTarget.ToSourceNavigator().Meta<JsonNode>()!["valueSet"]!.GetValue<string>()
            .ShouldBe("http://example.org/ValueSet/first");
    }
    [Fact]
    public void GivenSourceAndTargetAreTheSameJson_WhenAWriteIsFollowedByAnotherSourceRead_ThenTheLaterReadSeesTheWrite()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var resource = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","id":"Old","name":[{"family":"New"}]}""");
        var element = resource.ToElement(schema);
        var context = new MappingContext();
        context.SetSource("src", element);
        context.SetTarget("tgt", element);

        Execute(ParseMap("src.name.family -> tgt.id;\nsrc.id -> tgt.name.given;"), context, mutator);

        var actualJson = resource.ToSourceNavigator().Meta<JsonNode>()!.ToJsonString();
        actualJson.ShouldBe(
            """{"resourceType":"Patient","id":"New","name":[{"family":"New"},{"given":["New"]}]}""");
    }

    [Fact]
    public void GivenDependentGroupUsesSourceAsTarget_WhenQualifiedPathsAreWritten_ThenSourceAliasesRemainLive()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var source = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","id":"Old","name":[{"family":"New"}]}""");
        var target = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"separate-target"}""");
        var context = CreateContext(source, target, schema, schema);
        var map = ParseMap(
            "src -> tgt then Mutate(src, src);",
            """
            group Mutate(source src : Patient, target tgt : Patient) {
              src.name.family -> tgt.id;
              src.id -> tgt.name.given;
            }
            """);

        Execute(map, context, mutator);

        var actualSourceJson = source.ToSourceNavigator().Meta<JsonNode>()!.ToJsonString();
        actualSourceJson.ShouldBe(
            """{"resourceType":"Patient","id":"New","name":[{"family":"New"},{"given":["New"]}]}""");
        target.ToSourceNavigator().Meta<JsonNode>()!.ToJsonString().ShouldBe(
            """{"resourceType":"Patient","id":"separate-target"}""");
    }

    [Fact]
    public void GivenTargetChildrenWereReadBeforeMutation_WhenTargetChanges_ThenRefreshReturnsLatestValue()
    {
        var schema = FhirVersion.R4.GetSchemaProvider();
        var mutator = CreateMutator(schema);
        var patient = mutator.CreateElement("Patient");
        var priorNames = patient.Children("name");
        var name = mutator.SetTarget(patient, "name", mutator.CreateElement("HumanName",
            new Dictionary<string, object?> { ["family"] = "Initial" }));

        mutator.SetTarget(name, "family", mutator.CreateElement("string", "Latest"));

        priorNames.ShouldBeEmpty();
        var latestName = mutator.Refresh(patient).Children("name").ShouldHaveSingleItem();
        latestName.Children("family").ShouldHaveSingleItem().Value.ShouldBe("Latest");
        patient.Meta<JsonNode>()!["name"]![0]!["family"]!.GetValue<string>().ShouldBe("Latest");
    }

    private static JsonNodeMutator CreateMutator(ISchema schema) =>
        new(new FhirPathEvaluator(), new FhirPathParser(), () => schema);

    private static MappingContext CreateContext(
        ResourceJsonNode source,
        ResourceJsonNode target,
        ISchema sourceSchema,
        ISchema targetSchema)
    {
        var context = new MappingContext();
        context.SetSource("src", source.ToElement(sourceSchema));
        context.SetTarget("tgt", target.ToElement(targetSchema));
        return context;
    }

    private static MapExpression ParseMap(string rules, string additionalGroups = "") =>
        new MappingParser().Parse($$"""
            map 'http://example.org/xver-optimization-guards' = 'XverOptimizationGuards'
            group Main(source src : Patient, target tgt : Patient) {
              {{rules}}
            }
            {{additionalGroups}}
            """);

    private static void Execute(MapExpression map, MappingContext context, IMappingTargetMutator mutator) =>
        new MappingEvaluator(MappingEvaluatorOptions.Default, mutator).Execute(map, context);

    private static void Execute(MapExpression map, MappingContext context, MappingEvaluator evaluator) =>
        evaluator.Execute(map, context);

    private sealed class RefreshCountingMutator(IMappingTargetMutator inner) : IMappingTargetMutator
    {
        private readonly List<IElement> _refreshedElements = [];

        public int RefreshCount(IElement element) =>
            _refreshedElements.Count(refreshed => ReferenceEquals(refreshed, element));

        public IElement CreateElement(string typeName, object? value = null) => inner.CreateElement(typeName, value);
        public IType GetTargetDefinition(IElement parent, string propertyName) => inner.GetTargetDefinition(parent, propertyName);
        public IElement SetTarget(IElement parent, string propertyName, IElement? value,
            PropertyMutationMode mode = PropertyMutationMode.AutoDetect, bool insertFirst = false) =>
            inner.SetTarget(parent, propertyName, value, mode, insertFirst);
        public IElement Refresh(IElement element)
        {
            _refreshedElements.Add(element);
            return inner.Refresh(element);
        }
        public void MoveTarget(IElement element, int? index = null) => inner.MoveTarget(element, index);
        public void SetProperty(ResourceJsonNode resource, string path, IElement value,
            PropertyMutationMode mode = PropertyMutationMode.AutoDetect) => inner.SetProperty(resource, path, value, mode);
        public void SetProperty(ResourceJsonNode resource, string path, JsonNode value,
            PropertyMutationMode mode = PropertyMutationMode.AutoDetect) => inner.SetProperty(resource, path, value, mode);
        public JsonNode EnsurePropertyPath(ResourceJsonNode resource, string path) => inner.EnsurePropertyPath(resource, path);
        public void DeleteProperty(ResourceJsonNode resource, string path) => inner.DeleteProperty(resource, path);
        public void InsertIntoArray(ResourceJsonNode resource, string path, JsonNode value, int index) =>
            inner.InsertIntoArray(resource, path, value, index);
        public void ReplaceArrayElement(ResourceJsonNode resource, string path, JsonNode value) =>
            inner.ReplaceArrayElement(resource, path, value);
        public IEnumerable<JsonNode> Evaluate(ResourceJsonNode resource, string path) => inner.Evaluate(resource, path);
        public JsonNode EvaluateSingle(ResourceJsonNode resource, string path) => inner.EvaluateSingle(resource, path);
    }
    private sealed class ImmutableElement(
        string name,
        string instanceType,
        object? value = null,
        IReadOnlyList<IElement>? children = null) : IElement
    {
        private readonly IReadOnlyList<IElement> _children = children ?? [];

        public string Name { get; } = name;
        public string InstanceType { get; } = instanceType;
        public object? Value { get; } = value;
        public string Location => Name;
        public IType? Type => null;
        public bool HasPrimitiveValue => Value is not null;
        public IReadOnlyList<IElement> Children(string? name = null) => name is null
            ? _children
            : _children.Where(child => child.Name == name).ToList();
        public T? Meta<T>() where T : class => null;
    }
}
