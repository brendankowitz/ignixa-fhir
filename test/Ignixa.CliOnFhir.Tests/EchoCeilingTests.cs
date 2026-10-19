// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Commands;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 12b fix round 2, Q1: the four echoes the re-review found past the 16 KB ceiling, echoes measured as the envelope writes
/// them (<c>é</c> and <c>&lt;</c> are six bytes each), and the ceiling's last line of defence. Regressions: at dc1b5418 find's
/// elements (repeated in next, one warning per name), a long column name, a long reference and a cohort valueSet each answered
/// past the ceiling, a query echo of 2,048 characters could take 12 KB, and nothing stopped an answer a fit missed.
/// </summary>
[Collection("Console")]
public class EchoCeilingTests
{
    private const int Cap = 16 * 1024;

    private static readonly string Long = string.Concat(Enumerable.Repeat("abcdé<fghi", 2000));

    private static FolderSession Open(TestFolder folder) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, terminology: TerminologyOptions.None, maxResponseBytes: Cap);

    private static int Bytes(CallToolResult result) => Encoding.UTF8.GetByteCount(result.Content.OfType<TextContentBlock>().Single().Text);

    private static JsonNode Json(CallToolResult result) => JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!;

    [Fact]
    public void GivenALongElementsListBesideAFindThatMatches_WhenFound_ThenTheAnswerStaysWithinTheCeiling()
    {
        using TestFolder folder = SearchFixture.Clinic();
        string elements = string.Join(',', Enumerable.Range(1, 3000).Select(index => $"e{index}é<"));

        CallToolResult result = new PatientTools(Open(folder)).PatientContext("find", "p1", query: "8867-4", elements: elements);

        Bytes(result).ShouldBeLessThanOrEqualTo(Cap);
        JsonNode envelope = Json(result);
        ((int)envelope["facts"]!["total"]!).ShouldBeGreaterThan(0);
        envelope["issues"]!.AsArray().Count(issue => (string)issue!["code"]! == "element-ignored").ShouldBeLessThanOrEqualTo(Reading.ElementsProjection.MaxIgnoredListed + 1);
    }

    [Fact]
    public void GivenALongColumnName_WhenSearched_ThenItIsRefusedWithinTheCeiling()
    {
        using TestFolder folder = SearchFixture.Clinic();

        CallToolResult result = new ResourceTools(Open(folder)).SearchFhirResources("Observation", columns: new Dictionary<string, string> { [Long] = "id" });

        Bytes(result).ShouldBeLessThanOrEqualTo(Cap);
        ((string)Json(result)["issues"]![0]!["code"]!).ShouldBe("invalid-parameter");
    }

    [Fact]
    public void GivenAReferenceWithALongId_WhenRead_ThenItsAbsenceStaysWithinTheCeiling()
    {
        using TestFolder folder = SearchFixture.Clinic();

        CallToolResult result = new ResourceTools(Open(folder)).ReadResources(["Patient/" + Long]);

        Bytes(result).ShouldBeLessThanOrEqualTo(Cap);
    }

    [Theory]
    [InlineData(20000)]
    [InlineData(1000)]
    public async Task GivenALongCohortValueSet_WhenCounted_ThenTheAnswerStaysWithinTheCeiling(int length)
    {
        // A CodeSystem of the folder's own, so terminology loads and the valueSet is looked up (and found missing).
        using TestFolder folder = SearchFixture.Clinic().Write("cs.json", """{"resourceType":"CodeSystem","id":"cs","url":"http://example.org/cs","status":"active","content":"complete","concept":[{"code":"a"}]}""");
        JsonElement conditions = JsonSerializer.SerializeToElement(new[] { new { valueSet = "http://example.org/" + Long[..(length - 19)] } });

        CallToolResult result = await new PatientTools(Open(folder)).PatientCohort(conditions: conditions);

        Bytes(result).ShouldBeLessThanOrEqualTo(Cap);
        JsonNode issue = Json(result)["issues"]![0]!;
        ((string)issue["severity"]!).ShouldBe("error");
        ((string)issue["diagnostics"]!).Length.ShouldBeLessThan(600);
    }

    [Fact]
    public void GivenAQueryOfCharactersTheWriterEscapes_WhenEchoed_ThenTheEchoTakesAtMostTheLimitAsWritten()
    {
        string echoed = QueryEcho.Of(Long);

        QueryEcho.SerializedLength(echoed.TrimEnd('…')).ShouldBeLessThanOrEqualTo(QueryEcho.MaxLength);
        QueryEcho.IsCut(Long[..1200]).ShouldBeTrue();
        QueryEcho.IsCut(new string('a', QueryEcho.MaxLength)).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAToolWhoseAnswerIsOverTheCeiling_WhenCalled_ThenAnErrorEnvelopeReplacesIt()
    {
        using TestFolder folder = SearchFixture.Clinic();
        FolderSession session = Open(folder);
        McpServerTool big = McpServerTool.Create(() => new CallToolResult { Content = [new TextContentBlock { Text = new string('x', Cap + 1) }] }, new McpServerToolCreateOptions { Name = "big" });
        McpServerTool small = McpServerTool.Create(() => new CallToolResult { Content = [new TextContentBlock { Text = new string('x', Cap) }] }, new McpServerToolCreateOptions { Name = "small" });
        await using InProcessMcp mcp = await InProcessMcp.StartAsync([new CeilingTool(big, _ => session), new CeilingTool(small, _ => session)]);

        CallToolResult over = await mcp.Client.CallToolAsync("big", cancellationToken: mcp.CancellationToken);
        CallToolResult within = await mcp.Client.CallToolAsync("small", cancellationToken: mcp.CancellationToken);

        over.IsError.ShouldBe(true);
        JsonNode issue = Json(over)["issues"]![0]!;
        ((string)issue["code"]!).ShouldBe(ResponseCeiling.Code);
        ((string)issue["diagnostics"]!).ShouldStartWith($"big answered {Cap + 1} bytes, over the {Cap}-byte response ceiling");
        Bytes(within).ShouldBe(Cap);
    }

    [Fact]
    public void GivenAnEnvelopeOverTheCliCeiling_WhenWritten_ThenAnErrorEnvelopeReplacesItAndTheExitCodeIsOne()
    {
        var envelope = new ToolEnvelope<string?>(new string('x', EnvelopeJson.MaxBytes), [], [], [], [], new FreshnessInfo(IndexBuiltAt: null, StaleFiles: null));
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Console.SetOut(output);
        Console.SetError(errors);
        int exitCode;
        try
        {
            exitCode = EnvelopeOutput.Write(envelope);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(1);
        ((string)JsonNode.Parse(output.ToString())!["issues"]![0]!["code"]!).ShouldBe(ResponseCeiling.Code);
        errors.ToString().ShouldStartWith(ResponseCeiling.Code);
    }
}
