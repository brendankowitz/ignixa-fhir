// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.DeId;
using Ignixa.CliOnFhir.Mapping;
using Ignixa.CliOnFhir.Views;
using Ignixa.FhirPath.Attributes;
using Ignixa.FhirPath.Evaluation;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DeIdFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Iterating functions nested inside each other multiply their cost within one call of the FHIRPath evaluator, which no timeout can
/// interrupt: seven nested <c>select(...).count()</c> (274 characters) ran for minutes, and so do seven chained ones. Views,
/// de-identification rules and maps all refuse more than three levels and more than six calls, with names read as the FHIRPath parser
/// reads them.
/// </summary>
public class IteratingDepthGuardTests
{
    private const string Ten = "(1|2|3|4|5|6|7|8|9|10)";

    public static string Nested(int depth, string select = "select")
    {
        string expression = "1";
        for (int i = 0; i < depth; i++)
        {
            expression = $"{Ten}.{select}({expression}).count()";
        }

        return expression;
    }

    [Theory]
    [InlineData("name.where(use = 'official').given")]
    [InlineData("a.where(b.where(c.where(d.exists())))")]
    [InlineData("descendants().where(x.select(y))")]
    [InlineData("aggregate($this + $total, 0)")]
    [InlineData("(1|2|3).select((1|2).select((1|2).select(1).count()).count()).count()")]
    public void GivenThreeOrFewerIteratingLevels_WhenChecked_ThenTheExpressionIsAccepted(string expression) =>
        ViewExpressionGuards.Violation(expression).ShouldBeNull();

    [Theory]
    [InlineData("a.where(b.where(c.where(d.where(e))))")]
    [InlineData("a.select(b.select(c.select(d.descendants())))")]
    [InlineData("a.all(b.exists(c.repeat(d.aggregate($total))))")]
    [InlineData("a.select ( b.where ( c.sort ( d.select (e) ) ) )")]
    public void GivenMoreThanThreeIteratingLevels_WhenChecked_ThenTheExpressionIsRefused(string expression) =>
        ViewExpressionGuards.Violation(expression).ShouldNotBeNull().ShouldContain("more than 3 deep");

    // The four ways round the lexical reading of a0b1ed53, and repeatAll, which it did not know: each is a call the parser makes.
    [Theory]
    [InlineData("Select")]
    [InlineData("SELECT")]
    [InlineData("select/**/")]
    [InlineData("select /* (x) */ ")]
    [InlineData("select // a comment\n")]
    [InlineData("`select`")]
    [InlineData("\"select\"")]
    [InlineData("repeatAll")]
    [InlineData("RepeatAll")]
    [InlineData("`Where`")]
    public void GivenFourNestedCallsSpelledAsTheParserAlsoReadsThem_WhenChecked_ThenTheExpressionIsRefused(string select) =>
        ViewExpressionGuards.Violation(Nested(4, select)).ShouldNotBeNull().ShouldContain("more than 3 deep");

    // trace runs its projection once per item when a trace handler is set.
    [Fact]
    public void GivenTraceProjectionsNestedFourDeep_WhenChecked_ThenTheExpressionIsRefused()
    {
        string expression = "1";
        for (int i = 0; i < 4; i++)
        {
            expression = $"{Ten}.trace('t', {expression}).count()";
        }

        ViewExpressionGuards.Violation(expression).ShouldNotBeNull().ShouldContain("more than 3 deep");
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(10, true)]
    public void GivenChainedIteratingCalls_WhenChecked_ThenMoreThanSixAreRefused(int calls, bool refused)
    {
        string chain = Ten + string.Concat(Enumerable.Repeat($".select({Ten})", calls)) + ".count()";

        string? violation = ViewExpressionGuards.Violation(chain);

        if (refused)
        {
            violation.ShouldNotBeNull().ShouldContain("more than 6 times");
        }
        else
        {
            violation.ShouldBeNull();
        }
    }

    [Fact]
    public void GivenNestedAndChainedCallsTogether_WhenSevenInAll_ThenTheExpressionIsRefused() =>
        ViewExpressionGuards.Violation("a.where(b.where(c)).select(d.select(e)).exists(f).all(g).sort(h)").ShouldNotBeNull().ShouldContain("more than 6 times");

    // What the parser does not read as a call is not counted: quoted text, comments, a call with no argument.
    [Theory]
    [InlineData("name.where(use = 'select(select(select(select(x))))')")]
    [InlineData("name.where(use = 'official') /* select(select(select(select(x)))) */")]
    [InlineData("name.where(use = 'official') // select(select(select(select(x))))")]
    [InlineData("a.exists().select(b.exists()).where(c.exists()).all(d.exists()).sort().exists().exists().exists()")]
    [InlineData("a.select(b.select(c.select(d.select(e)))) # the tokenizer refuses '#', so the parser never builds these calls")]
    public void GivenCallsThatTheParserDoesNotMake_WhenChecked_ThenTheyAreNotCounted(string expression) =>
        ViewExpressionGuards.Violation(expression).ShouldBeNull();

    // A new FHIRPath function that takes an expression argument must be classified here, iterating or evaluated once.
    [Fact]
    public void GivenTheFhirPathFunctionsTakingExpressionArguments_WhenListed_ThenEachIsClassifiedAsIteratingOrEvaluatedOnce()
    {
        string[] functions = [.. typeof(FhirPathEvaluator).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
            .Select(method => method.GetCustomAttributes(typeof(FhirPathFunctionAttribute), inherit: false).OfType<FhirPathFunctionAttribute>().SingleOrDefault())
            .Where(attribute => attribute is { TakesExpressionArguments: true })
            .Select(attribute => attribute!.Name.ToUpperInvariant())
            .Distinct()
            .Order(StringComparer.Ordinal)];

        functions.ShouldNotBeEmpty();
        ViewExpressionGuards.IteratingWithArgument.Concat(ViewExpressionGuards.EvaluatedOnce).Select(name => name.ToUpperInvariant()).Order(StringComparer.Ordinal).ShouldBe(functions);
        ViewExpressionGuards.IteratingWithArgument.Intersect(ViewExpressionGuards.EvaluatedOnce).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenSevenChainedCalls_WhenADeIdentificationRulePath_ThenItIsRefusedAtOnce()
    {
        using TestFolder root = Root();
        string chain = Ten + string.Concat(Enumerable.Repeat($".select({Ten})", 7)) + ".count()";
        string config = $$$"""{"fhirPathRules":[{"path":"Patient.name.where({{{chain}}} > 0)","method":"redact"}]}""";

        IssueItem error = Error(await RunAsync(Data(root), new DeIdRequest(config, Out(root), TimeoutSeconds: 5)));

        error.Code.ShouldBe("deid-config-invalid");
        error.Diagnostics.ShouldContain("more than 6 times");
    }

    [Fact]
    public async Task GivenTheSevenDeepRepro_WhenRunAsAViewColumn_ThenItIsRefusedAtOnce()
    {
        using TestFolder root = Root();
        string view = $$$"""{"resourceType":"ViewDefinition","resource":"Patient","select":[{"column":[{"name":"n","path":"{{{Nested(7)}}}"}]}]}""";

        ToolEnvelope<ViewFacts?> envelope = await ViewFixture.RunInAsync(Data(root), new ViewRequest(view, TimeoutSeconds: 5));

        ViewFixture.Error(envelope).Code.ShouldBe("invalid-view");
    }

    [Fact]
    public async Task GivenTheSevenDeepRepro_WhenADeIdentificationRulePath_ThenItIsRefusedAtOnce()
    {
        using TestFolder root = Root();
        string config = $$$"""{"fhirPathRules":[{"path":"Patient.name.where({{{Nested(7)}}} > 0)","method":"redact"}]}""";

        IssueItem error = Error(await RunAsync(Data(root), new DeIdRequest(config, Out(root), TimeoutSeconds: 5)));

        error.Code.ShouldBe("deid-config-invalid");
        error.Diagnostics.ShouldContain("more than 3 deep");
    }

    [Fact]
    public async Task GivenTheSevenDeepRepro_WhenAFolderStructureMapCondition_ThenItIsRefusedAtOnce()
    {
        using TestFolder root = Root().Write("data/maps.ndjson", MapFixture.StructureMap("sm1", "http://example.org/sm/slow", Nested(7) + " > 0"));

        IssueItem error = MapFixture.Error(await MapFixture.RunAsync(Data(root), new MapRequest("StructureMap/sm1", TimeoutSeconds: 5)));

        error.Code.ShouldBe("map-invalid");
        error.Diagnostics.ShouldContain("more than 3 deep");
    }
}
