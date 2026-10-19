// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// A census of the MCP surface under <c>--toolset all</c>: every text argument of every tool, and of every operation of the
/// operation tools, given 20,000 characters (alone, as a value list for a search, and inside the JSON objects that hold text), must
/// answer within the 16 KB response cap by its own fit, not by the ceiling's last line of defence. The text mixes in <c>é</c> and
/// <c>&lt;</c>, which the envelope writes as six bytes each. The calls are listed from the menu, so a new tool or argument is covered
/// without editing this test. Regressions (Task 12b fix round 1): at 86a9d902 a long query through columns, a long OR list of
/// relative dates and a long folder_code_systems text each answered past the cap. Fix round 2: at dc1b5418 find's elements
/// (beside a query that matches), a long column name, a long reference and a cohort valueSet each did too.
/// </summary>
public class FreeTextCeilingCensusTests(ITestOutputHelper output)
{
    private const int Cap = 16 * 1024;

    private static readonly string Long = string.Concat(Enumerable.Repeat("abcdé<fghi", 2000));

    // A value for each argument a call needs besides the one under test.
    private static readonly Dictionary<string, object> Defaults = new(StringComparer.Ordinal)
    {
        ["resourceType"] = "Observation",
        ["patient"] = "p1",
        ["patientId"] = "p1",
        ["references"] = new[] { "Patient/p1" },
        ["topic"] = "search",
        ["system"] = "http://loinc.org",
        ["code"] = "8867-4",
        ["codeA"] = "8867-4",
        ["codeB"] = "8480-6",
        ["valueSet"] = "http://hl7.org/fhir/ValueSet/administrative-gender",
        ["text"] = "heart",
        ["classId"] = "C07",
        ["source"] = "ATC",
        ["nodeKey"] = "problems",
        ["query"] = "8867-4",
        ["package"] = "hl7.fhir.r4.core@4.0.1",
    };

    // Long values that are not plain strings: a search's query as an OR list and as many parameters, JSON arguments with text inside.
    private static IEnumerable<(string Argument, object Value)> Shaped(string tool) => tool switch
    {
        "read_resources" =>
        [
            ("references", new[] { "Patient/" + Long }),
            ("references", Enumerable.Range(1, 50).Select(index => $"Observation/{index}{Long[..300]}").ToArray()),
        ],
        "search_fhir_resources" or "plan_fhir_search" =>
        [
            ("query", "code=" + Long[..1000]),
            ("elements", string.Join(',', Enumerable.Range(1, 2000).Select(index => $"e{index}é<"))),
            ("query", "date=" + string.Join(',', Enumerable.Repeat("now", 3000))),
            ("query", string.Join('&', Enumerable.Range(1, 400).Select(index => $"date=ge2025-12-{1 + (index % 28):00}T00:00:00.1234567Z"))),
            ("query", "code=" + string.Join(',', Enumerable.Repeat("http://loinc.org|8867-4", 800))),
        ],
        _ => [],
    };

    private static IEnumerable<(string Argument, object Value)> ShapedJson(string tool) => tool switch
    {
        "search_fhir_resources" =>
        [
            ("columns", new Dictionary<string, string> { ["v"] = "value.ofType(Quantity).value", ["long"] = "'" + Long[..4000] + "'" }),
            ("columns", new Dictionary<string, string> { [Long] = "id" }),
            ("columns", Enumerable.Range(1, 20).ToDictionary(index => $"c{index}" + Long[..60], _ => "'" + Long[..90] + "'")),
        ],
        "patient_cohort" =>
        [
            ("conditions", JsonSerializer.SerializeToElement(new[] { new { codes = new[] { "http://snomed.info/sct|" + Long } } })),
            ("conditions", JsonSerializer.SerializeToElement(new[] { new { valueSet = Long } })),
            ("conditions", JsonSerializer.SerializeToElement(new[] { new { valueSet = "http://example.org/" + Long[..1000] } })),
        ],
        "draft_resource" => [("input", JsonSerializer.SerializeToElement(new { patient = "p1", code = new { system = "http://loinc.org", code = "8867-4" }, note = Long }))],
        _ => [],
    };

    [Fact]
    public async Task GivenEveryTextArgumentOfEveryTool_WhenGivenTwentyThousandCharacters_ThenTheResponseStaysWithinTheCap()
    {
        using TestFolder folder = SearchFixture.Clinic().Write("more.ndjson", string.Join('\n', Enumerable.Range(1, 60).Select(index =>
            $$$"""{"resourceType":"Observation","id":"m{{{index}}}","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/p1"},"effectiveDateTime":"2025-12-{{{1 + (index % 28):00}}}T00:00:00Z","valueQuantity":{"value":{{{index}}},"unit":"/min"}}""")));
        var session = new FolderSession(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, terminology: TerminologyOptions.None, maxResponseBytes: Cap);
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.All, McpServerSetup.ToolTypes, session, NullLoggerFactory.Instance);

        List<string> over = [];
        int calls = 0;
        foreach (McpClientTool tool in await mcp.Client.ListToolsAsync(cancellationToken: mcp.CancellationToken))
        {
            foreach (Dictionary<string, object?> arguments in Calls(tool))
            {
                calls++;
                CallToolResult result = await mcp.Client.CallToolAsync(tool.Name, arguments, cancellationToken: mcp.CancellationToken);
                string text = result.Content.OfType<TextContentBlock>().Single().Text;
                int bytes = Encoding.UTF8.GetByteCount(text);
                if (bytes > Cap || text.Contains(ResponseCeiling.Code, StringComparison.Ordinal))
                {
                    over.Add($"{tool.Name}({string.Join(", ", arguments.Select(argument => $"{argument.Key}={Shown(argument.Value)}"))}): {bytes} bytes {Largest(result)}");
                }
            }
        }

        output.WriteLine($"{calls} calls");
        over.ForEach(output.WriteLine);
        over.ShouldBeEmpty();
    }

    // The member that holds most of an oversized response, to say where the size went.
    private static string Largest(CallToolResult result)
    {
        System.Text.Json.Nodes.JsonObject envelope = System.Text.Json.Nodes.JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!.AsObject();
        (string Name, int Size) largest = envelope.Select(member => (member.Key, member.Value?.ToJsonString().Length ?? 0)).MaxBy(member => member.Item2);
        string code = envelope["issues"] is System.Text.Json.Nodes.JsonArray { Count: > 0 } issues ? (string)issues[0]!["code"]! : "-";
        return $"(largest {largest.Name} {largest.Size}; first issue {code})";
    }

    private static string Shown(object? value) => value is string text && text.Length > 40 ? $"<{text.Length} chars>" : value is string or null ? $"{value}" : "<json>";

    // One call per text argument (per operation, for the operation tools), with the others at their defaults.
    private static IEnumerable<Dictionary<string, object?>> Calls(McpClientTool tool)
    {
        if (tool.Name is "terminology" or "patient_context")
        {
            IReadOnlyList<ToolOperation> operations = tool.Name == "terminology" ? TerminologyTools.Operations : PatientTools.ContextOperations;
            foreach (ToolOperation operation in operations)
            {
                foreach (string argument in operation.Required.Concat(operation.Optional).Where(name => IsText(tool, name)).Append("operation"))
                {
                    Dictionary<string, object?> call = new(StringComparer.Ordinal) { ["operation"] = operation.Name };
                    foreach (string needed in operation.Required)
                    {
                        call[needed] = Defaults[needed];
                    }

                    call[argument] = Long;
                    yield return call;
                }
            }

            yield break;
        }

        JsonElement schema = tool.ProtocolTool.InputSchema;
        string[] required = schema.TryGetProperty("required", out JsonElement list) ? [.. list.EnumerateArray().Select(item => item.GetString()!)] : [];
        IEnumerable<(string Argument, object Value)> cases = [.. schema.GetProperty("properties").EnumerateObject()
            .Where(property => IsText(tool, property.Name))
            .Select(property => (property.Name, property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String && type.GetString() == "array" ? (object)new[] { Long } : Long)),
            .. Shaped(tool.Name), .. ShapedJson(tool.Name)];
        foreach ((string argument, object value) in cases)
        {
            Dictionary<string, object?> call = new(StringComparer.Ordinal);
            foreach (string name in required)
            {
                call[name] = name == "input" ? JsonSerializer.SerializeToElement(new { patient = "p1" }) : Defaults[name];
            }

            call[argument] = value;
            yield return call;
        }
    }

    private static bool IsText(McpClientTool tool, string name)
    {
        if (!tool.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.Object || !property.TryGetProperty("type", out JsonElement type))
        {
            return false;
        }

        IEnumerable<string?> types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(item => item.GetString()) : [type.GetString()];
        return types.Contains("string")
            || (types.Contains("array") && property.TryGetProperty("items", out JsonElement items) && items.TryGetProperty("type", out JsonElement itemType) && itemType.ToString().Contains("string", StringComparison.Ordinal));
    }
}
