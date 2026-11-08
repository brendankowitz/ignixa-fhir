/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * A repeat that leads back to the item it started from used to recurse until the stack overflowed, which ends the process and
 * cannot be caught. These tests run each such shape: every one must throw, not take the test host down.
 */

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Ignixa.SqlOnFhir.Evaluation;
using Shouldly;

#pragma warning disable CS0618 // Type or member is obsolete - ISourceNavigator used for legacy tests

namespace Ignixa.SqlOnFhir.Tests;

public class SqlOnFhirRepeatDepthTests
{
    private static readonly IFhirSchemaProvider SchemaProvider =
        FhirSpecificationExtensions.FromVersionString("4.0.1").GetSchemaProvider();

    [Theory]
    [InlineData("$this")]
    [InlineData("%rootResource")]
    public void GivenARepeatThatLeadsBackToItsOwnItem_WhenEvaluated_ThenItThrowsInsteadOfOverflowingTheStack(string repeat)
    {
        var evaluator = new SqlOnFhirEvaluator();
        IElement patient = Patient();
        ISourceNavigator view = View(repeat);

        Exception ex = Should.Throw<InvalidOperationException>(() => evaluator.EvaluateBatch(view, [patient]).ToList());

        Innermost(ex).Message.ShouldContain($"repeat went deeper than {SqlOnFhirEvaluationVisitor.MaxRepeatDepth} levels");
        Innermost(ex).Message.ShouldContain("a repeat path must lead to child elements");
    }

    [Fact]
    public void GivenTwoRepeatPathsOverADeepTree_WhenEvaluated_ThenTheTotalNodeCapStopsItBeforeTheDepthLimit()
    {
        // ["item","item"] doubles the visits per level: 2^30 nodes, never deeper than 30, so only a total cap can stop it.
        IElement element = ResourceJsonNode.Parse(DeepQuestionnaire(levels: 30).ToJsonString()).ToElement(SchemaProvider);
        const string view = """{"resource":"Questionnaire","select":[{"repeat":["item","item"],"column":[{"name":"id","path":"linkId"}]}]}""";

        Exception ex = Should.Throw<InvalidOperationException>(() =>
            new SqlOnFhirEvaluator().EvaluateBatch(JsonNodeSourceNode.Create(JsonNode.Parse(view)!, "ViewDefinition"), [element]).ToList());

        Innermost(ex).Message.ShouldContain("repeat produced more than ");
    }

    [Fact]
    public void GivenARecursiveStructureOfTwentyLevels_WhenRepeated_ThenAllLevelsAreRows()
    {
        // Questionnaire.item.item is recursive. (JSON of any real resource is at most 64 levels deep to parse, so a repeat over parsed
        // data stays far below the limit; only a path that leads back to its own item can reach it.)
        JsonObject questionnaire = DeepQuestionnaire(levels: 21);
        IElement element = ResourceJsonNode.Parse(questionnaire.ToJsonString()).ToElement(SchemaProvider);
        const string view = """{"resource":"Questionnaire","select":[{"repeat":["item"],"column":[{"name":"id","path":"linkId"}]}]}""";

        var rows = new SqlOnFhirEvaluator().EvaluateBatch(JsonNodeSourceNode.Create(JsonNode.Parse(view)!, "ViewDefinition"), [element]).ToList();

        rows.Count.ShouldBe(21);
    }

    // Questionnaire.item.item is recursive: a chain of groups ending in one leaf, `levels` items deep.
    private static JsonObject DeepQuestionnaire(int levels)
    {
        JsonNode item = new JsonObject { ["linkId"] = "leaf", ["type"] = "string" };
        for (int level = levels - 2; level >= 0; level--)
        {
            item = new JsonObject { ["linkId"] = $"l{level}", ["type"] = "group", ["item"] = new JsonArray(item) };
        }

        return new JsonObject { ["resourceType"] = "Questionnaire", ["status"] = "active", ["item"] = new JsonArray(item) };
    }

    private static IElement Patient() =>
        ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"p1"}""").ToElement(SchemaProvider);

    private static ISourceNavigator View(string repeat)
    {
        var view = new JsonObject
        {
            ["resource"] = "Patient",
            ["select"] = new JsonArray(new JsonObject
            {
                ["repeat"] = new JsonArray(repeat),
                ["column"] = new JsonArray(new JsonObject { ["name"] = "id", ["path"] = "id" }),
            }),
        };
        return JsonNodeSourceNode.Create(JsonNode.Parse(view.ToJsonString())!, "ViewDefinition");
    }

    private static Exception Innermost(Exception exception)
    {
        while (exception.InnerException is { } inner)
        {
            exception = inner;
        }

        return exception;
    }
}
