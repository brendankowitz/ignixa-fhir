// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// <c>--toolset query|all</c> (ADR 2610): what the query toolset leaves out of the menu, and that a call reaching for it fails
/// naming <c>--toolset all</c> instead of silently doing something else. New capability: before V14 every tool, operation and
/// mode was always offered.
/// </summary>
public class ToolsetTests
{
    private static TestFolder Labs()
    {
        var lines = new StringBuilder();
        foreach (string patient in new[] { "p1", "p2", "p3" })
        {
            lines.AppendLine($$"""{"resourceType":"Patient","id":"{{patient}}"}""");
            for (int day = 1; day <= 10; day++)
            {
                lines.AppendLine($$$"""{"resourceType":"Observation","id":"{{{patient}}}-o{{{day}}}","status":"final","subject":{"reference":"Patient/{{{patient}}}"},"code":{"coding":[{"system":"http://loinc.org","code":"4548-4"}]},"effectiveDateTime":"2020-01-{{{day:00}}}","valueQuantity":{"value":{{{5 + day / 10.0}}},"unit":"%"}}""");
            }
        }

        return new TestFolder().Write("labs.ndjson", lines.ToString());
    }

    [Fact]
    public async Task GivenTheQueryToolset_WhenToolsAreListed_ThenHiddenArgumentsAreNotInTheirSchemas()
    {
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.Query, McpServerSetup.ToolTypes, [null]);

        IList<McpClientTool> tools = await mcp.Client.ListToolsAsync(cancellationToken: mcp.CancellationToken);

        Properties(tools, "search_fhir_resources").ShouldNotContain("fhirpath");
        Properties(tools, "search_fhir_resources").ShouldContain("columns");
        Properties(tools, "search_fhir_resources").ShouldContain("perPatient");
        Properties(tools, "terminology").ShouldNotContain("codeA");
        Properties(tools, "terminology").ShouldNotContain("includeFolderUsage");
        Properties(tools, "terminology").ShouldContain("classId");
        Properties(tools, "folder_code_systems").ShouldBe(["count", "offset", "text", "resourceType", "system", "patient"], ignoreOrder: true);
        tools.Single(tool => tool.Name == "terminology").Description.ShouldNotContain("subsumes");
        tools.Single(tool => tool.Name == "search_fhir_resources").Description.ShouldNotContain("fhirpath", Case.Sensitive);
    }

    [Fact]
    public async Task GivenTheAllToolset_WhenToolsAreListed_ThenEveryArgumentIsInTheSchemas()
    {
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.All, McpServerSetup.ToolTypes, [null]);

        IList<McpClientTool> tools = await mcp.Client.ListToolsAsync(cancellationToken: mcp.CancellationToken);

        Properties(tools, "search_fhir_resources").ShouldContain("fhirpath");
        Properties(tools, "terminology").ShouldContain("codeA");
        tools.Single(tool => tool.Name == "terminology").Description.ShouldContain("subsumes");
    }

    [Fact]
    public async Task GivenTheQueryToolset_WhenFhirpathIsSent_ThenTheCallFailsNamingToolsetAll()
    {
        using TestFolder folder = Labs();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.Query, [typeof(ResourceTools)], Open(folder));

        JsonNode envelope = await CallAsync(mcp, "search_fhir_resources", new() { ["resourceType"] = "Observation", ["fhirpath"] = "entry.count()" });

        envelope["facts"].ShouldBeNull();
        string diagnostics = (string)envelope["issues"]![0]!["diagnostics"]!;
        diagnostics.ShouldContain("--toolset all");
        diagnostics.ShouldContain("columns");
    }

    [Fact]
    public async Task GivenTheAllToolset_WhenFhirpathIsSent_ThenItIsEvaluated()
    {
        using TestFolder folder = Labs();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.All, [typeof(ResourceTools)], Open(folder));

        JsonNode envelope = await CallAsync(mcp, "search_fhir_resources", new() { ["resourceType"] = "Observation", ["fhirpath"] = "entry.count()" });

        envelope["facts"]!["value"]!.ToJsonString().ShouldBe("[30]");
    }

    [Fact]
    public async Task GivenTheQueryToolset_WhenColumnsAndFhirpathAreBothSent_ThenTheColumnsAnswerWithAnAdjustedWarning()
    {
        using TestFolder folder = Labs();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.Query, [typeof(ResourceTools)], Open(folder));

        JsonNode envelope = await CallAsync(mcp, "search_fhir_resources", new()
        {
            ["resourceType"] = "Observation",
            ["compartment"] = "Patient/p1",
            ["columns"] = new Dictionary<string, string> { ["value"] = "valueQuantity.value" },
            ["elements"] = "code",
            ["fhirpath"] = "entry.count()",
        });

        envelope["facts"]!["rows"]!.AsArray().Count.ShouldBe(10);
        envelope["issues"]!.AsArray().Select(issue => (string)issue!["code"]!).ShouldBe(["argument-adjusted", "argument-adjusted"]);
    }

    [Fact]
    public async Task GivenTheQueryToolset_WhenAColumnCallsPatientSearch_ThenTheCallFailsPointingAtPatientCohort()
    {
        using TestFolder folder = Labs();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.Query, [typeof(ResourceTools)], Open(folder));

        JsonNode envelope = await CallAsync(mcp, "search_fhir_resources", new()
        {
            ["resourceType"] = "Patient",
            ["columns"] = new Dictionary<string, string> { ["labs"] = "patientSearch('Observation','code=4548-4').count()" },
        });

        envelope["facts"].ShouldBeNull();
        string diagnostics = (string)envelope["issues"]![0]!["diagnostics"]!;
        diagnostics.ShouldContain("--toolset all");
        diagnostics.ShouldContain("patient_cohort");
    }

    [Fact]
    public async Task GivenTheQueryToolset_WhenASearchIsCutIntoPages_ThenNoNextCallUsesFhirpath()
    {
        using TestFolder folder = Labs();
        await using InProcessMcp query = await InProcessMcp.StartAsync(McpToolset.Query, [typeof(ResourceTools)], Open(folder));
        await using InProcessMcp all = await InProcessMcp.StartAsync(McpToolset.All, [typeof(ResourceTools)], Open(folder));
        var arguments = new Dictionary<string, object?> { ["resourceType"] = "Observation", ["query"] = "code=4548-4" };

        JsonArray queryNext = (await CallAsync(query, "search_fhir_resources", arguments))["next"]!.AsArray();
        JsonArray allNext = (await CallAsync(all, "search_fhir_resources", arguments))["next"]!.AsArray();

        allNext.ShouldContain(call => call!["args"]!.AsObject().ContainsKey("fhirpath"));
        queryNext.ShouldNotContain(call => call!["args"]!.AsObject().ContainsKey("fhirpath"));
        queryNext.Count.ShouldBe(allNext.Count - 1);
    }

    [Theory]
    [InlineData("subsumes")]
    [InlineData("navigate")]
    [InlineData("validate")]
    [InlineData("list")]
    [InlineData("classes")]
    public async Task GivenTheQueryToolset_WhenAnAllOnlyTerminologyOperationIsCalled_ThenItFailsNamingToolsetAll(string operation)
    {
        using TestFolder folder = Labs();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.Query, [typeof(TerminologyTools)], Open(folder));

        JsonNode envelope = await CallAsync(mcp, "terminology", new() { ["operation"] = operation, ["system"] = "http://loinc.org", ["code"] = "4548-4" });

        string diagnostics = (string)envelope["issues"]![0]!["diagnostics"]!;
        diagnostics.ShouldBe($"operation={operation} is offered with --toolset all. This server offers lookup, search, expand, translate, class-search, class-members.");
    }

    [Fact]
    public async Task GivenTheQueryToolset_WhenAnUnknownOperationIsCalled_ThenTheMessageListsOnlyTheQueryOperations()
    {
        using TestFolder folder = Labs();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.Query, [typeof(TerminologyTools)], Open(folder));

        JsonNode envelope = await CallAsync(mcp, "terminology", new() { ["operation"] = "explode" });

        ((string)envelope["issues"]![0]!["diagnostics"]!).ShouldBe("'explode' is not an operation of this tool: lookup | search | expand | translate | class-search | class-members.");
    }

    [Theory]
    [InlineData(McpToolset.Query, "fhirpath", true)]
    [InlineData(McpToolset.Query, "patient_context", true)]
    [InlineData(McpToolset.Query, "patient_summary", false)]
    [InlineData(McpToolset.Query, "columns", false)]
    [InlineData(McpToolset.All, "fhirpath", false)]
    [InlineData(McpToolset.All, "patient_summary", false)]
    public async Task GivenAToolset_WhenAHelpTopicIsAskedFor_ThenOnlyItsOwnTopicsAreOffered(McpToolset toolset, string topic, bool isError)
    {
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(toolset, [typeof(HelpTools)]);

        CallToolResult result = await mcp.Client.CallToolAsync("help", new Dictionary<string, object?> { ["topic"] = topic }, cancellationToken: mcp.CancellationToken);

        (result.IsError == true).ShouldBe(isError);
    }

    [Fact]
    public async Task GivenAPatient_WhenFolderCodeTextIsScopedToIt_ThenOnlyItsCodesAreCountedAndNoCohortCallIsSuggested()
    {
        using TestFolder folder = new TestFolder().Write("data.ndjson", """
            {"resourceType":"Patient","id":"p1"}
            {"resourceType":"Patient","id":"p2"}
            {"resourceType":"Condition","id":"c1","subject":{"reference":"Patient/p1"},"code":{"coding":[{"system":"http://snomed.info/sct","code":"44054006","display":"Diabetes mellitus type 2"}]}}
            {"resourceType":"Condition","id":"c2","subject":{"reference":"Patient/p2"},"code":{"coding":[{"system":"http://snomed.info/sct","code":"44054006","display":"Diabetes mellitus type 2"}]}}
            {"resourceType":"Condition","id":"c3","subject":{"reference":"Patient/p2"},"code":{"coding":[{"system":"http://snomed.info/sct","code":"46635009","display":"Diabetes mellitus type 1"}]}}
            """);
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.Query, [typeof(FolderTools)], Open(folder));

        JsonNode scoped = await CallAsync(mcp, "folder_code_systems", new() { ["text"] = "diabet", ["patient"] = "p1" });
        JsonNode folderWide = await CallAsync(mcp, "folder_code_systems", new() { ["text"] = "diabet" });

        JsonNode code = scoped["facts"]!["codes"]!.AsArray().ShouldHaveSingleItem()!;
        ((string)code["code"]!).ShouldBe("sct|44054006");
        ((int)code["resources"]!).ShouldBe(1);
        ((int)code["patients"]!).ShouldBe(1);
        scoped["next"]!.AsArray().ShouldNotContain(call => (string)call!["tool"]! == "patient_cohort");
        folderWide["next"]!.AsArray().ShouldContain(call => (string)call!["tool"]! == "patient_cohort");
        folderWide["facts"]!["codes"]!.AsArray().Count.ShouldBe(2);
    }

    [Fact]
    public async Task GivenAPatientScopeWithoutText_WhenFolderCodeSystemsIsCalled_ThenItFailsAsInvalidParameter()
    {
        using TestFolder folder = Labs();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.Query, [typeof(FolderTools)], Open(folder));

        JsonNode envelope = await CallAsync(mcp, "folder_code_systems", new() { ["patient"] = "p1" });

        ((string)envelope["issues"]![0]!["code"]!).ShouldBe("invalid-parameter");
    }

    private static List<string> Properties(IList<McpClientTool> tools, string name) =>
        [.. JsonNode.Parse(tools.Single(tool => tool.Name == name).ProtocolTool.InputSchema.GetRawText())!["properties"]!.AsObject().Select(property => property.Key)];

    private static async Task<JsonNode> CallAsync(InProcessMcp mcp, string tool, Dictionary<string, object?> arguments)
    {
        CallToolResult result = await mcp.Client.CallToolAsync(tool, arguments, cancellationToken: mcp.CancellationToken);
        return JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!;
    }
}
