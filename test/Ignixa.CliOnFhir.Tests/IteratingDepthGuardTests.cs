// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.DeId;
using Ignixa.CliOnFhir.Mapping;
using Ignixa.CliOnFhir.Views;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DeIdFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Iterating functions nested inside each other multiply their cost within one call of the FHIRPath evaluator, which no timeout can
/// interrupt: seven nested <c>select(...).count()</c> (274 characters) ran for minutes. Views, de-identification rules and maps all
/// refuse more than three.
/// </summary>
public class IteratingDepthGuardTests
{
    private const string Ten = "(1|2|3|4|5|6|7|8|9|10)";

    public static string Nested(int depth)
    {
        string expression = "1";
        for (int i = 0; i < depth; i++)
        {
            expression = $"{Ten}.select({expression}).count()";
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
