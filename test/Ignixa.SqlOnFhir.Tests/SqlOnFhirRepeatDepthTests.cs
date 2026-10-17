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

#pragma warning disable CS0618 // Type or member is obsolete - ISourceNavigator used for legacy tests

namespace Ignixa.SqlOnFhir.Tests;

public class SqlOnFhirRepeatDepthTests
{
    private static readonly IFhirSchemaProvider _schemaProvider =
        FhirSpecificationExtensions.FromVersionString("4.0.1").GetSchemaProvider();

    [Theory]
    [InlineData("$this")]
    [InlineData("( $this )")]
    [InlineData("$this.where(true)")]
    [InlineData("$this | $this")]
    [InlineData("$this.first()")]
    [InlineData("select($this)")]
    [InlineData("%rootResource")]
    [InlineData("/* c */ $this")]
    [InlineData("$this // c")]
    public void GivenARepeatThatLeadsBackToItsOwnItem_WhenEvaluated_ThenItThrowsInsteadOfOverflowingTheStack(string repeat)
    {
        var evaluator = new SqlOnFhirEvaluator();
        IElement patient = Patient();
        ISourceNavigator view = View(repeat);

        Exception ex = Assert.ThrowsAny<InvalidOperationException>(() => evaluator.EvaluateBatch(view, [patient]).ToList());

        Assert.Contains("repeat went deeper than 100 levels", Innermost(ex).Message, StringComparison.Ordinal);
        Assert.Contains("a repeat path must lead to child elements", Innermost(ex).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenARepeatThatEndsWithinTheLimit_WhenEvaluated_ThenEveryLevelIsVisited()
    {
        var evaluator = new SqlOnFhirEvaluator();
        IElement patient = Patient();
        // Patient > contact is one level, and a Patient has no contact: only the root-level items exist.
        IEnumerable<Dictionary<string, object?>> rows = evaluator.EvaluateBatch(View("contact"), [patient]);

        Assert.Empty(rows);
    }

    [Fact]
    public void GivenARecursiveStructureOfTwentyLevels_WhenRepeated_ThenAllLevelsAreRows()
    {
        // Questionnaire.item.item is recursive. (JSON of any real resource is at most 64 levels deep to parse, so a repeat over parsed
        // data stays far below the limit; only a path that leads back to its own item can reach it.)
        JsonNode item = new JsonObject { ["linkId"] = "leaf", ["type"] = "string" };
        for (int level = 19; level >= 0; level--)
        {
            item = new JsonObject { ["linkId"] = $"l{level}", ["type"] = "group", ["item"] = new JsonArray(item) };
        }

        var questionnaire = new JsonObject { ["resourceType"] = "Questionnaire", ["status"] = "active", ["item"] = new JsonArray(item) };
        IElement element = ResourceJsonNode.Parse(questionnaire.ToJsonString()).ToElement(_schemaProvider);
        const string view = """{"resource":"Questionnaire","select":[{"repeat":["item"],"column":[{"name":"id","path":"linkId"}]}]}""";

        var rows = new SqlOnFhirEvaluator().EvaluateBatch(JsonNodeSourceNode.Create(JsonNode.Parse(view)!, "ViewDefinition"), [element]).ToList();

        Assert.Equal(21, rows.Count);
    }

    private static IElement Patient() =>
        ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"p1"}""").ToElement(_schemaProvider);

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
