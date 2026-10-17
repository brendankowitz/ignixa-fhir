// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 12b fix round 1, I4: <c>plan_fhir_search</c> takes search_fhir_resources' <c>asOf</c> (accepted though not listed, as the
/// all menu is at its budget), so the plan resolves relative dates as the search does, and refuses an argument it does not define.
/// Regression: at 86a9d902 the SDK dropped <c>asOf</c> (and any unknown argument) without a trace, so the plan resolved against
/// the session's asOf while the search it plans used the call's.
/// </summary>
public class PlanAsOfArgumentTests
{
    private static JsonNode Json(CallToolResult result) => JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!;

    private static Task<InProcessMcp> Start(TestFolder folder) =>
        InProcessMcp.StartAsync(McpToolset.All, [typeof(ResourceTools)], SearchFixture.Open(folder));

    [Fact]
    public async Task GivenAsOf_WhenAPlanResolvesARelativeDate_ThenItCountsFromTheCallsAsOfAsTheSearchDoes()
    {
        using TestFolder folder = SearchFixture.Clinic();
        await using InProcessMcp mcp = await Start(folder);
        Dictionary<string, object?> arguments = new() { ["resourceType"] = "Observation", ["query"] = "date=genow-7d", ["asOf"] = "2020-01-08" };

        JsonNode plan = Json(await mcp.Client.CallToolAsync("plan_fhir_search", arguments, cancellationToken: mcp.CancellationToken));
        JsonNode search = Json(await mcp.Client.CallToolAsync("search_fhir_resources", arguments, cancellationToken: mcp.CancellationToken));

        ((string)plan["facts"]!["query"]!).ShouldBe("date=ge2020-01-01T00:00:00Z");
        ((string)plan["facts"]!["query"]!).ShouldBe((string)search["facts"]!["query"]!);
        ((string)plan["asOf"]!["source"]!).ShouldBe("argument");
        ((string)plan["asOf"]!["instant"]!).ShouldBe("2020-01-08T00:00:00Z");
    }

    [Fact]
    public async Task GivenTheAllToolset_WhenThePlanToolIsListed_ThenAsOfIsNotInItsSchema()
    {
        using TestFolder folder = SearchFixture.Clinic();
        await using InProcessMcp mcp = await Start(folder);

        McpClientTool plan = (await mcp.Client.ListToolsAsync(cancellationToken: mcp.CancellationToken)).Single(tool => tool.Name == "plan_fhir_search");

        plan.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("asOf", out _).ShouldBeFalse();
        new HelpTools(McpToolset.All).Help("plan").Content.OfType<TextContentBlock>().Single().Text.ShouldContain("asOf (accepted though not listed)");
    }

    [Fact]
    public async Task GivenAnArgumentThePlanDoesNotDefine_WhenCalled_ThenItFailsNamingIt()
    {
        using TestFolder folder = SearchFixture.Clinic();
        await using InProcessMcp mcp = await Start(folder);

        JsonNode plan = Json(await mcp.Client.CallToolAsync(
            "plan_fhir_search",
            new Dictionary<string, object?> { ["resourceType"] = "Observation", ["_sort"] = "-date" },
            cancellationToken: mcp.CancellationToken));

        plan["facts"].ShouldBeNull();
        ((string)plan["issues"]![0]!["code"]!).ShouldBe("invalid-parameter");
        ((string)plan["issues"]![0]!["diagnostics"]!).ShouldContain("'_sort' is not an argument of this tool");
    }

    [Fact]
    public async Task GivenAMalformedAsOf_WhenAPlanIsAsked_ThenItFailsWithInvalidAsOf()
    {
        using TestFolder folder = SearchFixture.Clinic();
        await using InProcessMcp mcp = await Start(folder);

        JsonNode plan = Json(await mcp.Client.CallToolAsync(
            "plan_fhir_search",
            new Dictionary<string, object?> { ["resourceType"] = "Observation", ["asOf"] = "soon" },
            cancellationToken: mcp.CancellationToken));

        ((string)plan["issues"]![0]!["code"]!).ShouldBe("invalid-as-of");
    }

    // I5. Regression: at 86a9d902 a plan (or search) without a relative date ran as if a malformed --as-of had not been given.
    [Fact]
    public void GivenAMalformedSessionOption_WhenAPlanOrSearchWithoutARelativeDateRuns_ThenItFailsWithInvalidAsOf()
    {
        using TestFolder folder = SearchFixture.Clinic();
        var session = new FolderSession(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, asOfOption: "soon");

        SearchPlanner.Plan(session, new SearchRequest("Patient")).Issues.ShouldHaveSingleItem().Code.ShouldBe("invalid-as-of");
        ResourceSearcher.Search(session, new SearchRequest("Patient")).Issues.ShouldHaveSingleItem().Code.ShouldBe("invalid-as-of");
    }
}
