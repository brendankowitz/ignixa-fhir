// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DraftFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 12b, item 5: <c>draft_resource</c>, the <c>draft</c> verb over MCP under <c>--toolset all</c> only: the draft is returned,
/// never written, and is answered whole within the response cap. New behaviour: before it there was no such tool.
/// </summary>
public class DraftToolTests
{
    private static JsonNode Json(CallToolResult result) => JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!;

    private static JsonElement Input(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static DraftTools Tools(FolderSession session) => new(session, NullLoggerFactory.Instance);

    private static string[] Snapshot(string root) => [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(entry => $"{Path.GetRelativePath(root, entry)}:{(File.Exists(entry) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry))) : "dir")}")];

    [Fact]
    public async Task GivenAPotassiumOrder_WhenDraftedOverMcp_ThenTheCheckedDraftIsReturnedAndNothingIsWritten()
    {
        using TestFolder folder = Clinic();
        string[] before = Snapshot(folder.Path);

        JsonNode envelope = Json(await Tools(Open(folder)).DraftResource("MedicationRequest", Input(PotassiumOrder)));

        ((string)envelope["facts"]!["verdict"]!).ShouldBe("valid");
        ((string)envelope["facts"]!["resource"]!["resourceType"]!).ShouldBe("MedicationRequest");
        envelope["facts"]!["file"].ShouldBeNull();
        Snapshot(folder.Path).ShouldBe(before);
    }

    [Fact]
    public async Task GivenAsOfAndProfileInTheInput_WhenDrafted_ThenTheyDateAndProfileTheDraftAndAreNotMembersOfIt()
    {
        using TestFolder folder = ClinicWithProfile();
        JsonObject input = JsonNode.Parse(PotassiumResult)!.AsObject();
        input["asOf"] = "2023-03-04T05:06:07Z";
        input["profile"] = CategoryRequired;

        JsonNode envelope = Json(await Tools(Open(folder)).DraftResource("Observation", Input(input.ToJsonString())));

        JsonNode resource = envelope["facts"]!["resource"]!;
        ((string)resource["effectiveDateTime"]!).ShouldBe("2023-03-04T05:06:07Z");
        ((string)resource["meta"]!["profile"]![0]!).ShouldBe(CategoryRequired);
        resource["asOf"].ShouldBeNull();
        ((string)envelope["asOf"]!["source"]!).ShouldBe(AsOfSources.Argument);
    }

    [Fact]
    public async Task GivenNoAsOf_WhenDraftedUnderASessionOption_ThenTheSessionsAsOfDatesIt()
    {
        using TestFolder folder = Clinic();
        var session = new FolderSession(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, terminology: TerminologyOptions.None, asOfOption: "2022-02-02");

        JsonNode envelope = Json(await Tools(session).DraftResource("MedicationRequest", Input(PotassiumOrder)));

        ((string)envelope["facts"]!["resource"]!["authoredOn"]!).ShouldBe("2022-02-02");
        ((string)envelope["asOf"]!["source"]!).ShouldBe(AsOfSources.SessionOption);
    }

    [Theory]
    [InlineData("\"/etc/passwd\"")]
    [InlineData("[1]")]
    public async Task GivenAnInputThatIsNotAnObject_WhenDrafted_ThenItIsRefusedAndNoFileIsRead(string input)
    {
        using TestFolder folder = Clinic();

        CallToolResult result = await Tools(Open(folder)).DraftResource("MedicationRequest", Input(input));

        result.IsError.ShouldBe(true);
        ((string)Json(result)["issues"]![0]!["code"]!).ShouldBe("draft-input-invalid");
    }

    [Fact]
    public async Task GivenAnOutMemberInTheInput_WhenDrafted_ThenItIsRefusedAsAMemberTheDraftDoesNotHave()
    {
        using TestFolder folder = Clinic();
        JsonObject input = JsonNode.Parse(PotassiumOrder)!.AsObject();
        input["out"] = Path.GetTempPath();

        CallToolResult result = await Tools(Open(folder)).DraftResource("MedicationRequest", Input(input.ToJsonString()));

        result.IsError.ShouldBe(true);
        ((string)Json(result)["issues"]![0]!["code"]!).ShouldBe("draft-input-invalid");
    }

    [Fact]
    public async Task GivenACapTooSmallForTheDraft_WhenDrafted_ThenItIsRefusedWholeRatherThanCut()
    {
        using TestFolder folder = Clinic();
        var session = new FolderSession(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, terminology: TerminologyOptions.None, maxResponseBytes: EnvelopeJson.MinBudget, asOfOption: Instant);

        CallToolResult result = await Tools(session).DraftResource("MedicationRequest", Input(PotassiumOrder));

        result.IsError.ShouldBe(true);
        ((string)Json(result)["issues"]![0]!["code"]!).ShouldBe("draft-too-large");
        System.Text.Encoding.UTF8.GetByteCount(result.Content.OfType<TextContentBlock>().Single().Text).ShouldBeLessThanOrEqualTo(EnvelopeJson.MinBudget);
    }

    [Theory]
    [InlineData(McpToolset.All, true)]
    [InlineData(McpToolset.Query, false)]
    public async Task GivenAToolset_WhenListed_ThenDraftResourceIsOfferedUnderAllOnlyWithItsTwoArguments(McpToolset toolset, bool offered)
    {
        using TestFolder folder = Clinic();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(toolset, McpServerSetup.ToolTypes, Open(folder), NullLoggerFactory.Instance);

        IList<McpClientTool> tools = await mcp.Client.ListToolsAsync(cancellationToken: mcp.CancellationToken);

        McpClientTool? draft = tools.SingleOrDefault(tool => tool.Name == "draft_resource");
        (draft is not null).ShouldBe(offered);
        if (draft is not null)
        {
            draft.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ShouldBe(["resourceType", "input"]);
            draft.ProtocolTool.Annotations!.ReadOnlyHint.ShouldBe(true);
        }
    }

    [Fact]
    public async Task GivenTheAllToolset_WhenADraftIsCalledOverTheProtocol_ThenItAnswersWithTheEnvelope()
    {
        using TestFolder folder = Clinic();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.All, [typeof(DraftTools)], Open(folder), NullLoggerFactory.Instance);

        CallToolResult result = await mcp.Client.CallToolAsync(
            "draft_resource",
            new Dictionary<string, object?> { ["resourceType"] = "ServiceRequest", ["input"] = Input(MagnesiumOrder) },
            cancellationToken: mcp.CancellationToken);

        JsonNode envelope = Json(result);
        ((string)envelope["facts"]!["resource"]!["resourceType"]!).ShouldBe("ServiceRequest");
        envelope["asOf"].ShouldNotBeNull();
    }
}
