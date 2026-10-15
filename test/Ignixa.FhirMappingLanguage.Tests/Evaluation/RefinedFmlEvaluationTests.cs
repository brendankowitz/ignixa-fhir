/*
 * Copyright (c) 2026, Ignixa Contributors
 */

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Registry;
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

public class RefinedFmlEvaluationTests
{
    [Fact]
    public void GivenHelperGroups_WhenExecutingTheMap_ThenOnlyTheEntryGroupRunsAutomatically()
    {
        var (actual, _) = Execute("""
            group Main(source src, target tgt) {
              src.id as v -> tgt.id = v;
            }
            group Uncalled(source missing, target other) {
              missing -> other.active = false;
            }
            """, """{"resourceType":"Patient","id":"entry","active":true}""");

        AssertJson(actual, """{"resourceType":"Patient","id":"entry"}""");
    }

    [Fact]
    public void GivenNestedAliases_WhenIteratingNames_ThenWritesStayInTheirOwnTargetsAndSourceIsUnchanged()
    {
        const string Input = """{"resourceType":"Patient","name":[{"family":"Alpha","given":["A","B"]},{"family":"Beta","given":["C"]}]}""";
        var (actual, source) = Execute("""
            group Main(source src, target tgt) {
              src.name as n -> tgt.name as out then {
                n.family as f -> out.family = f;
                n.given as g -> out.given = g;
              };
            }
            """, Input);

        AssertJson(actual, Input);
        AssertJson(source, Input);
    }

    [Fact]
    public void GivenTwoSources_WhenExecutingTheRule_ThenEveryOrderedCombinationRuns()
    {
        var (actual, _) = Execute("""
            group Main(source src, target tgt) {
              src.name as n, src.identifier as i -> tgt.name as out then {
                n.family as f -> out.text = (f & ':' & %i.value);
              };
            }
            """, """{"resourceType":"Patient","name":[{"family":"A"},{"family":"B"}],"identifier":[{"value":"1"},{"value":"2"}]}""");

        AssertJson(actual, """{"resourceType":"Patient","name":[{"text":"A:1"},{"text":"A:2"},{"text":"B:1"},{"text":"B:2"}]}""");
    }

    [Fact]
    public void GivenShadowedVariablesInDependentGroups_WhenInvokingNextGroup_ThenCallerBindingsAreRestored()
    {
        var (actual, _) = Execute("""
            group Main(source src, target tgt) {
              src.name as name -> tgt.name as out then Family(name, out), Text(name, out);
            }
            group Family(source src, target tgt) {
              src.family as name -> tgt.family = name;
            }
            group Text(source src, target tgt) {
              src.family as value -> tgt.text = value;
            }
            """, """{"resourceType":"Patient","name":[{"family":"Alpha"},{"family":"Beta"}]}""");

        AssertJson(actual, """{"resourceType":"Patient","name":[{"family":"Alpha","text":"Alpha"},{"family":"Beta","text":"Beta"}]}""");
    }

    [Fact]
    public void GivenDefaultGroupsWithTheSameSourceType_WhenCopying_ThenTheTargetTypeSelectsTheGroup()
    {
        var (actual, _) = Execute("""
            group Main(source src, target tgt) { src.name -> tgt.name; }
            group Wrong(source src : HumanName, target tgt : Identifier) <<types>> {
              src.family as f -> tgt.value = f;
            }
            group Correct(source src : HumanName, target tgt : HumanName) <<types>> {
              src.family as f -> tgt.text = f;
            }
            """, """{"resourceType":"Patient","name":[{"family":"Alpha","given":["omit"]}]}""");

        AssertJson(actual, """{"resourceType":"Patient","name":[{"text":"Alpha"}]}""");
    }

    [Fact]
    public void GivenEvaluate_WhenTheArgumentIsAStringLiteral_ThenItIsNotExecutedAsFhirPath()
    {
        var (actual, _) = Execute("""
            group Main(source src, target tgt) {
              src -> tgt.name as out then {
                src -> out.text = evaluate(src, 'id.upper()');
                src -> out.family = evaluate(src, id.upper());
              };
            }
            """, """{"resourceType":"Patient","id":"literal"}""");

        AssertJson(actual, """{"resourceType":"Patient","name":[{"text":"id.upper()","family":"LITERAL"}]}""");
    }

    [Fact]
    public void GivenSharedChildRules_WhenTheParentMatchesTwice_ThenEachIterationHasItsOwnSharedTarget()
    {
        var (actual, _) = Execute("""
            group Main(source src, target tgt) {
              src.name as n -> tgt then {
                n.family as f -> tgt.name as out share name then { f -> out.family = f; };
                n.family as f -> tgt.name as out share name then { f -> out.text = f; };
              };
            }
            """, """{"resourceType":"Patient","name":[{"family":"Alpha"},{"family":"Beta"}]}""");

        AssertJson(actual, """{"resourceType":"Patient","name":[{"family":"Alpha","text":"Alpha"},{"family":"Beta","text":"Beta"}]}""");
    }

    [Theory]
    [InlineData(0, 0, """{"resourceType":"Patient"}""")]
    [InlineData(1, 0, """{"resourceType":"Patient","name":[{"family":"Alpha"}]}""")]
    [InlineData(0, 2, """{"resourceType":"Patient","name":[{"text":"ID2"}]}""")]
    [InlineData(1, 1, """{"resourceType":"Patient","name":[{"family":"Alpha","text":"ID1"}]}""")]
    [InlineData(2, 1, """{"resourceType":"Patient","name":[{"family":"Beta","text":"ID1"}]}""")]
    [InlineData(1, 2, """{"resourceType":"Patient","name":[{"family":"Alpha","text":"ID2"}]}""")]
    [InlineData(2, 2, """{"resourceType":"Patient","name":[{"family":"Beta","text":"ID2"}]}""")]
    public void GivenSiblingRulesSharingATarget_WhenSourceCountsVary_ThenAllMatchesReuseOneInstance(
        int nameCount, int identifierCount, string expected)
    {
        string[] families = ["Alpha", "Beta"];
        string[] identifiers = ["ID1", "ID2"];
        var input = new JsonObject
        {
            ["resourceType"] = "Patient",
            ["name"] = new JsonArray(families.Take(nameCount).Select(f => new JsonObject { ["family"] = f }).ToArray()),
            ["identifier"] = new JsonArray(identifiers.Take(identifierCount).Select(id => new JsonObject { ["value"] = id }).ToArray())
        };

        var (actual, _) = Execute("""
            group Main(source src, target tgt) {
              src.name as n -> tgt.name as out share common then {
                n.family as f -> out.family = f;
              };
              src.identifier as i -> tgt.name as out share common then {
                i.value as v -> out.text = v;
              };
            }
            """, input.ToJsonString());

        AssertJson(actual, expected);
    }

    [Fact]
    public void GivenRepeatedSourceMatches_WhenAppendingToASharedTarget_ThenAllValuesAccumulateOnOneInstance()
    {
        var (actual, _) = Execute("""
            group Main(source src, target tgt) {
              src.name as n -> tgt.name as out share common then {
                n.family as f -> out.given = f;
              };
            }
            """, """{"resourceType":"Patient","name":[{"family":"Alpha"},{"family":"Beta"}]}""");

        AssertJson(actual, """{"resourceType":"Patient","name":[{"given":["Alpha","Beta"]}]}""");
    }

    [Fact]
    public void GivenAParentSharedBinding_WhenChildAndSiblingRulesReuseIt_ThenTheyUpdateTheSameInstance()
    {
        var (actual, _) = Execute("""
            group Main(source src, target tgt) {
              src -> tgt.name as parent share common then {
                src.name as n -> tgt.name as child share common then {
                  n.family as f -> child.family = f;
                };
              };
              src.id as id -> tgt.name as sibling share common then {
                id -> sibling.text = id;
              };
            }
            """, """{"resourceType":"Patient","id":"parent","name":[{"family":"Alpha"},{"family":"Beta"}]}""");

        AssertJson(actual, """{"resourceType":"Patient","name":[{"family":"Beta","text":"parent"}]}""");
    }

    [Fact]
    public void GivenSeparateDependentBlocks_WhenTheyDeclareTheSameSharedLabel_ThenBindingsDoNotLeak()
    {
        var (actual, _) = Execute("""
            group Main(source src, target tgt) {
              src -> tgt then {
                src.id as v -> tgt.name as out share common then { v -> out.family = v; };
              };
              src -> tgt then {
                src.id as v -> tgt.name as out share common then { v -> out.text = v; };
              };
              src.id as v -> tgt.name as out share common then { v -> out.given = v; };
            }
            """, """{"resourceType":"Patient","id":"value"}""");

        AssertJson(actual, """{"resourceType":"Patient","name":[{"family":"value"},{"text":"value"},{"given":["value"]}]}""");
    }

    [Fact]
    public void GivenAParentSharedBinding_WhenCalledGroupsUseTheSameLabel_ThenEachInvocationIsIndependent()
    {
        var (actual, _) = Execute("""
            group Main(source src, target tgt) {
              src.id as v -> tgt.name as out share common then { v -> out.family = v; };
              src -> tgt then Helper(src, tgt), Helper(src, tgt);
              src.id as v -> tgt.name as out share common then { v -> out.text = v; };
            }
            group Helper(source src, target tgt) {
              src.id as v -> tgt.name as out share common then { v -> out.given = v; };
            }
            """, """{"resourceType":"Patient","id":"value"}""");

        AssertJson(actual, """{"resourceType":"Patient","name":[{"family":"value","text":"value"},{"given":["value"]},{"given":["value"]}]}""");
    }

    [Fact]
    public void GivenChainedTargetsInSiblingRules_WhenExecuting_ThenIntermediateCollectionsAreNotImplicitlyShared()
    {
        var (actual, _) = Execute("""
            group Main(source src, target tgt) {
              src.id as v -> tgt.name.given = v;
              src.id as v -> tgt.name.family = v;
            }
            """, """{"resourceType":"Patient","id":"value"}""");

        AssertJson(actual, """{"resourceType":"Patient","name":[{"given":["value"]},{"family":"value"}]}""");
    }

    [Fact]
    public void GivenSingleTargetMode_WhenTheRuleWouldCreateMultipleTargets_ThenExecutionFails()
    {
        var exception = Should.Throw<InvalidOperationException>(() => Execute("""
            group Main(source src, target tgt) {
              src.name -> tgt.name single;
            }
            """, """{"resourceType":"Patient","name":[{"family":"Alpha"},{"family":"Beta"}]}"""));

        exception.Message.ShouldContain("single");
    }

    [Fact]
    public void GivenFirstAndLastTargetModes_WhenOtherRulesAppend_ThenMarkedSegmentsKeepTheirOrder()
    {
        var (actual, _) = Execute("""
            group Main(source src, target tgt) {
              src.name as n -> tgt.name = n first;
              src.name last as n -> tgt.name = n last;
              src.id as v -> tgt.name as out then { v -> out.text = v; };
            }
            """, """{"resourceType":"Patient","id":"middle","name":[{"family":"Alpha"},{"family":"Beta"}]}""");

        AssertJson(actual, """{"resourceType":"Patient","name":[{"family":"Alpha"},{"family":"Beta"},{"text":"middle"},{"family":"Beta"}]}""");
    }

    [Theory]
    [InlineData("first")]
    [InlineData("last")]
    public void GivenTwoRulesWithTheSameOrderingMode_WhenTargetingOneCollection_ThenExecutionFails(string mode)
    {
        var exception = Should.Throw<InvalidOperationException>(() => Execute($$"""
            group Main(source src, target tgt) {
              src.name first as n -> tgt.name = n {{mode}};
              src.name last as n -> tgt.name = n {{mode}};
            }
            """, """{"resourceType":"Patient","name":[{"family":"Alpha"},{"family":"Beta"}]}"""));

        exception.Message.ShouldContain($"Multiple rules specify '{mode}'");
    }

    [Fact]
    public void GivenPrimitiveMetadata_WhenLastItemsAreReordered_ThenShadowsMoveWithTheirValues()
    {
        var (actual, _) = Execute("""
            group Main(source src, target tgt) {
              src.name as n -> tgt.name as out then {
                n.given first as a -> out.given = a last;
                n.given last as b -> out.given = b;
              };
            }
            """, """
            {"resourceType":"Patient","name":[{"given":["A","B"],
              "_given":[{"extension":[{"url":"http://example.org/note","valueString":"one"}]},null]}]}
            """);

        AssertJson(actual, """
            {"resourceType":"Patient","name":[{"given":["B","A"],
              "_given":[null,{"extension":[{"url":"http://example.org/note","valueString":"one"}]}]}]}
            """);
    }

    [Fact]
    public void GivenTypeFilteredSourceWithADefault_WhenNoMatchingValueExists_ThenDefaultIsUsed()
    {
        var (actual, _) = Execute("""
            group Main(source src, target tgt) {
              src.active : string 1..1 default ('unknown') as v -> tgt.gender = v;
            }
            """, """{"resourceType":"Patient","active":true}""");

        AssertJson(actual, """{"resourceType":"Patient","gender":"unknown"}""");
    }

    [Fact]
    public async Task GivenWildcardImports_WhenResolving_ThenMatchingRegisteredGroupsAreAvailable()
    {
        var parser = new MappingParser();
        var main = parser.Parse("""
            map 'http://example.org/Main' = 'Main'
            imports 'http://example.org/Shared*'
            group Main(source src, target tgt) { src -> tgt then Copy(src, tgt); }
            """);
        var dependency = parser.Parse("""
            map 'http://example.org/SharedPatient' = 'Shared'
            group Copy(source src, target tgt) { src.id as v -> tgt.id = v; }
            """);
        var registry = new MapRegistry();
        registry.Register(dependency);
        var resolver = new ImportResolver(registry, parser);

        await resolver.ResolveImportsAsync(main);

        resolver.FindGroup(main, "Copy").ShouldBeSameAs(dependency.Groups[0]);
        resolver.FindOwningMap(dependency.Groups[0]).ShouldBeSameAs(dependency);
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => new ImportResolver(new MapRegistry(), parser).ResolveImportsAsync(main));
        exception.Message.ShouldContain("No registered maps match");
    }

    private static (ResourceJsonNode Target, ResourceJsonNode Source) Execute(string groups, string input)
    {
        var schema = FhirVersion.R5.GetSchemaProvider();
        var source = ResourceJsonNode.Parse(input);
        var target = ResourceJsonNode.Parse("""{"resourceType":"Patient"}""");
        var mutator = new JsonNodeMutator(new FhirPathEvaluator(), new FhirPathParser(), () => schema);
        var context = new MappingContext { ResourceCreator = type => mutator.CreateElement(type) };
        context.SetSource("src", source.ToElement(schema));
        context.SetTarget("tgt", target.ToElement(schema));
        context.SetTargetResource("tgt", target);
        var map = new MappingParser().Parse("map 'http://example.org/Test' = 'Test'\n" + groups);

        new MappingEvaluator(MappingEvaluatorOptions.Default, mutator).Execute(map, context);

        context.Errors.ShouldBeEmpty();
        return (target, source);
    }

    private static void AssertJson(ResourceJsonNode actual, string expected) =>
        CanonicalJson.Canonicalize(actual.MutableNode().ToJsonString()).ShouldBe(CanonicalJson.Canonicalize(expected));
}
