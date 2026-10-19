// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Drafting;
using Ignixa.CliOnFhir.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DraftFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 12b fix round 1: how a client finds <c>draft_resource</c>'s arguments. I9: <c>input</c> is described as an object;
/// <c>profile</c> and <c>asOf</c> sent beside it are moved into it with an <c>argument-adjusted</c> warning; a member error names the
/// call's members too. M8: the input is re-serialised without escaping non-ASCII text. M9: a profile not loaded names the MCP way to
/// load it (<c>input.packages</c>). Regressions: at 86a9d902 the schema said <c>true</c> (anything), <c>asOf</c> beside input was an
/// unknown argument, member errors listed only the draft's members, a 3,000-character non-ASCII note was refused as too large, and
/// the profile remedy named the CLI's <c>--package</c>.
/// </summary>
public class DraftToolArgumentTests
{
    private static JsonNode Json(CallToolResult result) => JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!;

    private static JsonElement Element(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task<JsonNode> CallAsync(TestFolder folder, Dictionary<string, object?> arguments)
    {
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.All, [typeof(DraftTools)], Open(folder), NullLoggerFactory.Instance);
        return Json(await mcp.Client.CallToolAsync("draft_resource", arguments, cancellationToken: mcp.CancellationToken));
    }

    [Fact]
    public async Task GivenTheAllToolset_WhenListed_ThenInputIsDescribedAsAnObject()
    {
        using TestFolder folder = Clinic();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.All, [typeof(DraftTools)], Open(folder), NullLoggerFactory.Instance);

        McpClientTool draft = (await mcp.Client.ListToolsAsync(cancellationToken: mcp.CancellationToken)).Single(tool => tool.Name == "draft_resource");

        draft.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("input").GetRawText().ShouldBe("""{"type":"object"}""");
    }

    [Fact]
    public async Task GivenAsOfAndProfileBesideInput_WhenDrafted_ThenTheyAreReadFromInputWithAWarningEach()
    {
        using TestFolder folder = ClinicWithProfile();

        JsonNode envelope = await CallAsync(folder, new()
        {
            ["resourceType"] = "Observation",
            ["input"] = Element(PotassiumResult),
            ["asOf"] = "2023-03-04T05:06:07Z",
            ["profile"] = CategoryRequired,
        });

        JsonNode resource = envelope["facts"]!["resource"]!;
        ((string)resource["effectiveDateTime"]!).ShouldBe("2023-03-04T05:06:07Z");
        ((string)resource["meta"]!["profile"]![0]!).ShouldBe(CategoryRequired);
        string[] warnings = [.. envelope["issues"]!.AsArray().Where(issue => (string)issue!["code"]! == "argument-adjusted").Select(issue => (string)issue!["diagnostics"]!)];
        warnings.Length.ShouldBe(2);
        warnings.ShouldContain(text => text.StartsWith("'asOf' was given beside 'input' and was read as input.asOf", StringComparison.Ordinal));
        warnings.ShouldContain(text => text.StartsWith("'profile' was given beside 'input' and was read as input.profile", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenAsOfBothBesideAndInInput_WhenDrafted_ThenItIsRefusedAsGivenTwice()
    {
        using TestFolder folder = Clinic();
        JsonObject input = JsonNode.Parse(PotassiumOrder)!.AsObject();
        input["asOf"] = "2023-01-01";

        JsonNode envelope = await CallAsync(folder, new() { ["resourceType"] = "MedicationRequest", ["input"] = Element(input.ToJsonString()), ["asOf"] = "2024-01-01" });

        envelope["facts"].ShouldBeNull();
        ((string)envelope["issues"]![0]!["code"]!).ShouldBe("invalid-parameter");
        ((string)envelope["issues"]![0]!["diagnostics"]!).ShouldContain("given both beside 'input' and in it");
    }

    [Fact]
    public async Task GivenAnUnknownMemberOfInput_WhenDrafted_ThenTheErrorNamesTheCallsMembersToo()
    {
        using TestFolder folder = Clinic();
        JsonObject input = JsonNode.Parse(PotassiumOrder)!.AsObject();
        input["asof"] = "2023-01-01";

        JsonNode envelope = await CallAsync(folder, new() { ["resourceType"] = "MedicationRequest", ["input"] = Element(input.ToJsonString()) });

        string diagnostics = (string)envelope["issues"]![0]!["diagnostics"]!;
        ((string)envelope["issues"]![0]!["code"]!).ShouldBe(DraftArguments.InvalidCode);
        diagnostics.ShouldContain("Did you mean 'asOf'?");
        diagnostics.ShouldContain(", profile, packages, asOf.");
    }

    [Theory]
    [InlineData("asOf", "20230101", "'input.asOf' must be text")]
    [InlineData("profile", "{\"url\":\"x\"}", "'input.profile' must be text")]
    [InlineData("packages", "\"hl7.fhir.us.core@6.1.0\"", "'input.packages' must be a list")]
    public async Task GivenACallMemberOfTheWrongKind_WhenDrafted_ThenItIsRefusedSayingWhatItMustBe(string member, string value, string expected)
    {
        using TestFolder folder = Clinic();
        JsonObject input = JsonNode.Parse(PotassiumOrder)!.AsObject();
        input[member] = JsonNode.Parse(value);

        JsonNode envelope = await CallAsync(folder, new() { ["resourceType"] = "MedicationRequest", ["input"] = Element(input.ToJsonString()) });

        ((string)envelope["issues"]![0]!["code"]!).ShouldBe(DraftArguments.InvalidCode);
        ((string)envelope["issues"]![0]!["diagnostics"]!).ShouldContain(expected);
    }

    [Fact]
    public async Task GivenANonAsciiNoteWithinTheInputCap_WhenDraftedOverMcp_ThenItIsNotInflatedPastTheCap()
    {
        using TestFolder folder = Clinic();
        JsonObject input = JsonNode.Parse(PotassiumOrder)!.AsObject();
        input["note"] = new string('é', 3000);

        JsonNode envelope = await CallAsync(folder, new() { ["resourceType"] = "MedicationRequest", ["input"] = Element(input.ToJsonString()) });

        envelope["facts"].ShouldNotBeNull();
        ((string)envelope["facts"]!["resource"]!["note"]![0]!["text"]!).ShouldBe(new string('é', 3000));
    }

    [Fact]
    public async Task GivenAProfileThatIsNotLoaded_WhenDraftedOverMcp_ThenTheRemedyNamesInputPackages()
    {
        using TestFolder folder = Clinic();
        JsonObject input = JsonNode.Parse(PotassiumResult)!.AsObject();
        input["profile"] = "http://example.org/fhir/StructureDefinition/not-here";

        JsonNode envelope = await CallAsync(folder, new() { ["resourceType"] = "Observation", ["input"] = Element(input.ToJsonString()) });

        ((string)envelope["issues"]![0]!["code"]!).ShouldBe("profile-not-loaded");
        ((string)envelope["issues"]![0]!["diagnostics"]!).ShouldContain("with input.packages");
    }

    [Fact]
    public async Task GivenAProfileThatIsNotLoaded_WhenDraftedOnTheCli_ThenTheRemedyNamesThePackageOption()
    {
        using TestFolder folder = Clinic();

        var envelope = await RunAsync(folder, new DraftRequest("Observation", PotassiumResult, "http://example.org/fhir/StructureDefinition/not-here"));

        Error(envelope).Diagnostics.ShouldContain("with --package");
    }
}
