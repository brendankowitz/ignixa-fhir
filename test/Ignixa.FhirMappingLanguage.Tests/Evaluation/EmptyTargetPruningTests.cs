using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Tests.Conformance;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.TestSupport;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class EmptyTargetPruningTests
{
    [Fact]
    public void GivenEmptyAuditEventSourceIdentifier_WhenExecutingGroup_ThenTargetCascadeIsPrunedAndSourceIsUnchanged()
    {
        const string input = """{"resourceType":"AuditEvent","source":{"identifier":{}}}""";
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src.source.identifier as identifier -> tgt.source.identifier = identifier;
            }
            """, input, "AuditEvent", FhirVersion.Stu3);

        ExecuteGroup(execution);

        AssertJson(execution.Target, """{"resourceType":"AuditEvent"}""");
        AssertJson(execution.Source, input);
    }

    [Fact]
    public void GivenAPopulatedPrimitive_WhenItsValueIsCleared_ThenMetadataSurvivesWithoutANullValue()
    {
        const string input = """{"resourceType":"Patient","birthDate":"2024","_birthDate":{"id":"metadata"}}""";
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src.birthDate as input -> tgt.birthDate = input as date then {
                src -> date.value = create('date');
              };
            }
            """, input, "Patient");

        Execute(execution);

        AssertJson(execution.Target, """{"resourceType":"Patient","_birthDate":{"id":"metadata"}}""");
        AssertJson(execution.Source, input);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void GivenContractTermProducesNoValue_WhenExecuting_ThenPruningOptionControlsEmptyTermRetention(
        bool pruneEmptyTargets, bool executeGroup)
    {
        const string input = """{"resourceType":"Patient","name":[{}]}""";
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src.name as name -> tgt.term as term then {
                name.family as family -> term.text = family;
              };
            }
            """, input, "Contract");

        var options = new MappingEvaluatorOptions { PruneEmptyTargets = pruneEmptyTargets };
        if (executeGroup)
            ExecuteGroup(execution, options);
        else
            Execute(execution, options);

        AssertJson(execution.Target, pruneEmptyTargets
            ? """{"resourceType":"Contract"}"""
            : """{"resourceType":"Contract","term":[{}]}""");
        AssertJson(execution.Source, input);
    }

    [Fact]
    public void GivenContractHasPopulatedAndEmptyTerms_WhenExecuting_ThenOnlyPopulatedTermRemains()
    {
        const string input = """{"resourceType":"Patient","name":[{"family":"keep"},{}]}""";
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src.name as name -> tgt.term as term then {
                name.family as family -> term.text = family;
              };
            }
            """, input, "Contract");

        Execute(execution);

        AssertJson(execution.Target, """{"resourceType":"Contract","term":[{"text":"keep"}]}""");
        AssertJson(execution.Source, input);
    }

    [Fact]
    public void GivenEarlierRuleCreatesEmptySharedAlias_WhenLaterSiblingPopulatesIt_ThenAliasSurvivesGroupCompletion()
    {
        const string input = """{"resourceType":"Patient","id":"filled-by-sibling"}""";
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src -> tgt.name as out share common then {
                src.name as name -> out.family = name.family;
              };
              src.id as id -> tgt.name as out share common then {
                id -> out.family = id;
              };
            }
            """, input, "Patient");

        Execute(execution);

        AssertJson(execution.Target, """{"resourceType":"Patient","name":[{"family":"filled-by-sibling"}]}""");
        AssertJson(execution.Source, input);
    }

    [Fact]
    public void GivenTargetAliasStartsEmpty_WhenDependentRulePopulatesIt_ThenPopulatedTargetSurvivesGroupCompletion()
    {
        const string input = """{"resourceType":"Patient","id":"filled-by-dependent"}""";
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src -> tgt.name as out then {
                src.id as id -> out.family = id;
              };
            }
            """, input, "Patient");

        Execute(execution);

        AssertJson(execution.Target, """{"resourceType":"Patient","name":[{"family":"filled-by-dependent"}]}""");
        AssertJson(execution.Source, input);
    }

    [Fact]
    public void GivenPrimitiveArrayHasMetadataAndNullPadding_WhenExecuting_ThenValuesAndShadowsAreCompactedTogether()
    {
        const string input = """
            {"resourceType":"Patient","name":[{"given":["first","second"],"_given":[null,
              {"extension":[{"url":"http://example.org/second","valueString":"second metadata"}]},null,
              {"extension":[{"url":"http://example.org/shadow-only","valueString":"kept"}]}]}]}
            """;
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src.name as name -> tgt.name as out then {
                name.given as given -> out.given = given;
              };
            }
            """, input, "Patient");

        Execute(execution);

        AssertJson(execution.Target, """
            {"resourceType":"Patient","name":[{"given":["first","second",null],"_given":[null,
              {"extension":[{"url":"http://example.org/second","valueString":"second metadata"}]},
              {"extension":[{"url":"http://example.org/shadow-only","valueString":"kept"}]}]}]}
            """);
        AssertJson(execution.Source, input);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GivenUntouchedTargetContent_WhenExecuting_ThenPruningOptionControlsNormalization(bool pruneEmptyTargets)
    {
        const string input = """{"resourceType":"Patient","id":"source"}""";
        const string targetInput = """{"resourceType":"Patient","name":[{"_given":[null,{"id":"metadata"},null],"given":["value",null,null]}]}""";
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src.id as id -> tgt.id = id;
            }
            """, input, "Patient", targetInput: targetInput);

        Execute(execution, new MappingEvaluatorOptions { PruneEmptyTargets = pruneEmptyTargets });

        var name = execution.Target.MutableNode()["name"]![0]!.AsObject();
        name.Select(property => property.Key).ShouldBe(["_given", "given"]);
        AssertJson(execution.Target, pruneEmptyTargets
            ? """{"resourceType":"Patient","id":"source","name":[{"_given":[null,{"id":"metadata"}],"given":["value",null]}]}"""
            : """{"resourceType":"Patient","id":"source","name":[{"_given":[null,{"id":"metadata"},null],"given":["value",null,null]}]}""");
        AssertJson(execution.Source, input);
    }

    [Fact]
    public void GivenCopiedPrimitiveShadowPrecedesValues_WhenExecuting_ThenPairedArraysAndPropertyOrderArePreserved()
    {
        const string input = """{"resourceType":"Patient","name":[{"_given":[null,{"id":"metadata"},null],"given":["value",null,null]}]}""";
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src.name as name -> tgt.name = name;
            }
            """, input, "Patient");

        Execute(execution);

        var name = execution.Target.MutableNode()["name"]![0]!.AsObject();
        name.Select(property => property.Key).ShouldBe(["_given", "given"]);
        AssertJson(execution.Target, """{"resourceType":"Patient","name":[{"_given":[null,{"id":"metadata"}],"given":["value",null]}]}""");
        AssertJson(execution.Source, input);
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public void GivenACopiedSubtreeWithNestedEmpties_WhenExecuting_ThenOnlyItsPopulatedDescendantsRemain(FhirVersion version)
    {
        const string input = """
            {"resourceType":"Patient","name":[{},
              {"family":"keep","given":[null,"first",null,"second",null],
               "extension":[{}, {"url":"http://example.org/keep","extension":[{}]}, {}]},
              {"extension":[{"extension":[{}]}]}]}
            """;
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src.name as name -> tgt.name = name;
            }
            """, input, "Patient", version);

        Execute(execution);

        AssertJson(execution.Target, """
            {"resourceType":"Patient","name":[{"family":"keep","given":["first","second"],
              "extension":[{"url":"http://example.org/keep"}]}]}
            """);
        AssertJson(execution.Source, input);
    }

    [Fact]
    public void GivenAnEmptySharedTargetBeforeAPopulatedSibling_WhenLaterPopulated_ThenItsOriginalPositionIsPreserved()
    {
        const string input = """{"resourceType":"Patient","id":"A"}""";
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src -> tgt.name as a share common then {
                src.name as name -> a.family = name.family;
              };
              src -> tgt.name as b then { src -> b.family = 'B'; };
              src.id as id -> tgt.name as a share common then { id -> a.family = id; };
            }
            """, input, "Patient");

        Execute(execution);

        AssertJson(execution.Target, """{"resourceType":"Patient","name":[{"family":"A"},{"family":"B"}]}""");
        AssertJson(execution.Source, input);
    }

    [Fact]
    public void GivenAnEmptyChoiceReplacedByAnotherChoice_WhenExecuting_ThenThePopulatedChoiceSurvives()
    {
        const string input = """{"resourceType":"Patient"}""";
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src -> tgt.extension as e then {
                src -> e.value = create('Coding');
                src -> e.value = 'kept';
              };
            }
            """, input, "Patient");

        Execute(execution);

        AssertJson(execution.Target, """{"resourceType":"Patient","extension":[{"valueString":"kept"}]}""");
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public void GivenPruningIsDisabled_WhenCopyingNestedEmptyAndPrimitiveMetadataContent_ThenContentIsUnchanged(FhirVersion version)
    {
        const string input = """
            {"resourceType":"Patient","name":[{},
              {"family":"keep","given":["value",null,null],"_given":[null,{"id":"metadata"},null],
               "extension":[{}, {"extension":[{}]}]}]}
            """;
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src.name as name -> tgt.name = name;
            }
            """, input, "Patient", version);

        Execute(execution, new MappingEvaluatorOptions { PruneEmptyTargets = false });

        AssertJson(execution.Target, input);
        AssertJson(execution.Source, input);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GivenAPrepopulatedTarget_WhenExecutingGroup_ThenPruningOptionControlsNullPropertiesAndEmptyContainers(bool pruneEmptyTargets)
    {
        const string input = """{"resourceType":"Patient","id":"source"}""";
        const string targetInput = """{"resourceType":"Patient","birthDate":null,"_birthDate":{},"extension":[],"name":[{}]}""";
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src.id as id -> tgt.id = id;
            }
            """, input, "Patient", targetInput: targetInput);

        ExecuteGroup(execution, new MappingEvaluatorOptions { PruneEmptyTargets = pruneEmptyTargets });

        AssertJson(execution.Target, pruneEmptyTargets
            ? """{"resourceType":"Patient","id":"source"}"""
            : """{"resourceType":"Patient","id":"source","birthDate":null,"_birthDate":{},"extension":[],"name":[{}]}""");
        AssertJson(execution.Source, input);
    }

    [Theory]
    [InlineData("null", "{}", "{}")]
    [InlineData("null", "{\"id\":\"metadata\"}", "{\"_birthDate\":{\"id\":\"metadata\"}}")]
    [InlineData("\"2024\"", "{}", "{\"birthDate\":\"2024\"}")]
    [InlineData("null", "{\"extension\":[{}]}", "{}")]
    public void GivenAPrimitiveWithEmptyOrPopulatedMetadata_WhenCopied_ThenOnlyActualContentSurvives(
        string value, string shadow, string expected)
    {
        string input = $$"""{"resourceType":"Patient","birthDate":{{value}},"_birthDate":{{shadow}}}""";
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src.birthDate as date -> tgt.birthDate = date;
            }
            """, input, "Patient");

        Execute(execution);

        var expectedTarget = JsonNode.Parse(expected)!.AsObject();
        expectedTarget["resourceType"] = "Patient";
        AssertJson(execution.Target, expectedTarget.ToJsonString());
        AssertJson(execution.Source, input);
    }

    [Theory]
    [InlineData("[null,null,null]", "[{}, {\"id\":\"metadata\"}, {}]", "{\"_given\":[{\"id\":\"metadata\"}]}")]
    [InlineData("[\"A\",null,\"B\",null]", "[{}, {}, {}, {}]", "{\"given\":[\"A\",\"B\"]}")]
    [InlineData("[\"A\"]", "[null,{\"id\":\"metadata\"},null]", "{\"given\":[\"A\",null],\"_given\":[null,{\"id\":\"metadata\"}]}")]
    public void GivenCopiedPrimitiveArrays_WhenCompacting_ThenMetadataOnlyItemsAndAlignmentArePreserved(
        string values, string shadows, string expected)
    {
        string input = $$"""{"resourceType":"Patient","name":[{"given":{{values}},"_given":{{shadows}}}]}""";
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src.name as name -> tgt.name = name;
            }
            """, input, "Patient");

        Execute(execution);

        AssertJson(execution.Target, $$"""{"resourceType":"Patient","name":[{{expected}}]}""");
        AssertJson(execution.Source, input);
    }

    [Fact]
    public void GivenFalseZeroAndScalarValues_WhenExecuting_ThenValuesArePreserved()
    {
        const string input = """{"resourceType":"Patient","id":"preserved","active":false,"multipleBirthInteger":0}""";
        var execution = Prepare("""
            group Main(source src, target tgt) {
              src.id as id -> tgt.id = id;
              src.active as active -> tgt.active = active;
              src.multipleBirthInteger as count -> tgt.multipleBirthInteger = count;
            }
            """, input, "Patient");

        Execute(execution);

        AssertJson(execution.Target, input);
        AssertJson(execution.Source, input);
    }

    [Fact]
    public void GivenEvaluatorIsReusedAcrossExecuteAndExecuteGroup_WhenEachRunCreatesAnEmptyAlias_ThenBothTargetsArePrunedIndependently()
    {
        const string groups = """
            group Main(source src, target tgt) {
              src -> tgt.name as out then {
                src.name as name -> out.family = name.family;
              };
            }
            """;
        const string firstInput = """{"resourceType":"Patient","id":"first"}""";
        const string secondInput = """{"resourceType":"Patient","id":"second"}""";
        var first = Prepare(groups, firstInput, "Patient");
        var second = Prepare(groups, secondInput, "Patient");
        var evaluator = new MappingEvaluator(MappingEvaluatorOptions.Default, first.Mutator);

        evaluator.Execute(first.Map, first.Context);
        evaluator.ExecuteGroup(second.Map, "Main", second.Context);
        first.Context.Errors.ShouldBeEmpty();
        second.Context.Errors.ShouldBeEmpty();

        AssertJson(first.Target, """{"resourceType":"Patient"}""");
        AssertJson(second.Target, """{"resourceType":"Patient"}""");
        AssertJson(first.Source, firstInput);
        AssertJson(second.Source, secondInput);
    }

    [Fact]
    public void GivenEvaluatorIsReused_WhenPruningOptionChanges_ThenEachExecutionHonorsItsSetting()
    {
        const string groups = """
            group Main(source src, target tgt) {
              src -> tgt.name as out;
            }
            """;
        const string input = """{"resourceType":"Patient"}""";
        var first = Prepare(groups, input, "Patient");
        var second = Prepare(groups, input, "Patient");
        var options = new MappingEvaluatorOptions { PruneEmptyTargets = false };
        var evaluator = new MappingEvaluator(options, first.Mutator);

        evaluator.Execute(first.Map, first.Context);
        options.PruneEmptyTargets = true;
        evaluator.ExecuteGroup(second.Map, "Main", second.Context);

        first.Context.Errors.ShouldBeEmpty();
        second.Context.Errors.ShouldBeEmpty();
        AssertJson(first.Target, """{"resourceType":"Patient","name":[{}]}""");
        AssertJson(second.Target, """{"resourceType":"Patient"}""");
        AssertJson(first.Source, input);
        AssertJson(second.Source, input);
    }

    [Theory]
    [InlineData("""{"resourceType":"Patient","name":[{}]}""", """{"resourceType":"Patient"}""", false, true)]
    [InlineData("""{"resourceType":"Patient","name":[{}]}""", """{"resourceType":"Patient"}""", true, true)]
    [InlineData("""{"resourceType":"Patient","name":[{}]}""", """{"resourceType":"Patient","name":[{}]}""", true, false)]
    public void GivenLegacyMutatorAndNonJsonTarget_WhenCopyingAnEmptyElement_ThenPruningOptionIsHonored(
        string input, string expected, bool append, bool pruneEmptyTargets)
    {
        var schema = FhirVersion.R5.GetSchemaProvider();
        var source = ResourceJsonNode.Parse(input);
        var targetResource = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        var innerMutator = new JsonNodeMutator(new FhirPathEvaluator(), new FhirPathParser(), () => schema);
        var mutator = new ForwardingJsonNodeMutator(innerMutator);
        var context = new MappingContext();
        context.SetSource("src", source.ToElement(schema));
        context.SetTarget("tgt", new ImmutableTargetElement());
        context.SetTargetResource("tgt", targetResource);
        string listMode = append ? "last" : "";
        var map = new MappingParser().Parse($$"""
            map 'http://example.org/LegacyTargetCleanup' = 'LegacyTargetCleanup'
            group Main(source src, target tgt) {
              src.name as name -> tgt.name = name {{listMode}};
            }
            """);

        new MappingEvaluator(new MappingEvaluatorOptions { PruneEmptyTargets = pruneEmptyTargets }, mutator).Execute(map, context);

        context.Errors.ShouldBeEmpty();
        AssertJson(targetResource, expected);
        AssertJson(source, input);
    }
    private static Evaluation Prepare(
        string groups,
        string input,
        string targetResourceType,
        FhirVersion version = FhirVersion.R5,
        string? targetInput = null)
    {
        var schema = version.GetSchemaProvider();
        var mutator = new JsonNodeMutator(new FhirPathEvaluator(), new FhirPathParser(), () => schema);
        var source = ResourceJsonNode.Parse(input);
        var target = ResourceJsonNode.Parse(targetInput ?? $$"""{"resourceType":"{{targetResourceType}}"}""");
        var context = new MappingContext { ResourceCreator = type => mutator.CreateElement(type) };
        context.SetSource("src", source.ToElement(schema));
        context.SetTarget("tgt", target.ToElement(schema));
        context.SetTargetResource("tgt", target);
        var map = new MappingParser().Parse("map 'http://example.org/EmptyTargetPruning' = 'EmptyTargetPruning'\n" + groups);
        return new Evaluation(source, target, context, map, mutator);
    }

    private static void Execute(Evaluation execution, MappingEvaluatorOptions? options = null)
    {
        new MappingEvaluator(options ?? MappingEvaluatorOptions.Default, execution.Mutator).Execute(execution.Map, execution.Context);
        execution.Context.Errors.ShouldBeEmpty();
    }

    private static void ExecuteGroup(Evaluation execution, MappingEvaluatorOptions? options = null)
    {
        new MappingEvaluator(options ?? MappingEvaluatorOptions.Default, execution.Mutator)
            .ExecuteGroup(execution.Map, "Main", execution.Context);
        execution.Context.Errors.ShouldBeEmpty();
    }

    private static void AssertJson(ResourceJsonNode actual, string expected) =>
        CanonicalJson.Canonicalize(actual.MutableNode().ToJsonString())
            .ShouldBe(CanonicalJson.Canonicalize(expected));

    private sealed class ForwardingJsonNodeMutator(IJsonNodeMutator inner) : IJsonNodeMutator
    {
        public void SetProperty(ResourceJsonNode resource, string fhirPathExpression, IElement value, PropertyMutationMode mode = PropertyMutationMode.AutoDetect) =>
            inner.SetProperty(resource, fhirPathExpression, value, mode);

        public void SetProperty(ResourceJsonNode resource, string fhirPathExpression, JsonNode value, PropertyMutationMode mode = PropertyMutationMode.AutoDetect) =>
            inner.SetProperty(resource, fhirPathExpression, value, mode);

        public JsonNode EnsurePropertyPath(ResourceJsonNode resource, string fhirPathExpression) =>
            inner.EnsurePropertyPath(resource, fhirPathExpression);

        public void DeleteProperty(ResourceJsonNode resource, string fhirPathExpression) =>
            inner.DeleteProperty(resource, fhirPathExpression);

        public void InsertIntoArray(ResourceJsonNode resource, string fhirPathExpression, JsonNode value, int index) =>
            inner.InsertIntoArray(resource, fhirPathExpression, value, index);

        public void ReplaceArrayElement(ResourceJsonNode resource, string fhirPathExpression, JsonNode value) =>
            inner.ReplaceArrayElement(resource, fhirPathExpression, value);

        public IEnumerable<JsonNode> Evaluate(ResourceJsonNode resource, string fhirPathExpression) =>
            inner.Evaluate(resource, fhirPathExpression);

        public JsonNode EvaluateSingle(ResourceJsonNode resource, string fhirPathExpression) =>
            inner.EvaluateSingle(resource, fhirPathExpression);
    }

    private sealed class ImmutableTargetElement : IElement
    {
        public string Name => "Patient";
        public object? Value => null;
        public string InstanceType => "Patient";
        public string Location => "Patient";
        public IType? Type => null;
        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();
        public T? Meta<T>() where T : class => null;
        public bool HasPrimitiveValue => false;
    }
    private sealed record Evaluation(
        ResourceJsonNode Source,
        ResourceJsonNode Target,
        MappingContext Context,
        Ignixa.FhirMappingLanguage.Expressions.MapExpression Map,
        JsonNodeMutator Mutator);
}
