// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Drafting;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>
/// <c>draft_resource</c>, offered under <c>--toolset all</c> only: the CLI <c>draft</c> verb's <see cref="DraftService"/> without
/// <c>--out</c>, so over MCP a draft is only ever returned, never written. Its input is the typed JSON object of the CLI's
/// <c>--input</c>, always inline (a string is refused rather than read as a path), plus <c>profile</c> and <c>asOf</c> (the CLI's
/// <c>--profile</c> and the call's own asOf); the members are in the <c>draft</c> help topic.
/// </summary>
[McpServerToolType]
public sealed class DraftTools(FolderSession session, ILoggerFactory loggerFactory)
{
    /// <summary>The members of <c>input</c> that are the call's, not the draft's: taken out before the draft reads it.</summary>
    internal static readonly IReadOnlyList<string> CallMembers = ["profile", "packages", "asOf"];

    // The arguments the tool takes, and the call members, which are moved into input when sent beside it.
    private static readonly IReadOnlySet<string> DraftArgumentNames =
        new HashSet<string>([.. ToolArguments.Names(typeof(DraftTools).GetMethod(nameof(DraftResource))!), .. CallMembers], StringComparer.Ordinal);

    // Non-ASCII text stays as written, so the input the draft reads is no larger than the one sent (its size is capped).
    private static readonly JsonSerializerOptions Unescaped = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [McpServerTool(Name = "draft_resource", ReadOnly = true, Idempotent = true)]
    [Description("Checks a draft order or result. help: draft")]
    public async Task<CallToolResult> DraftResource(
        string resourceType,
        [ObjectArgument] JsonElement input,
        RequestContext<CallToolRequestParams>? context = null,
        CancellationToken cancellationToken = default)
    {
        if (ToolArguments.Unknown(DraftArgumentNames, context?.Params?.Arguments?.Keys) is { } unknown)
        {
            return ToolResults.From(EnvelopeAssembler.Failure<DraftFacts>(session, new IssueItem("error", "invalid-parameter", unknown, Location: null), cancellationToken));
        }

        if (input.ValueKind != JsonValueKind.Object)
        {
            return ToolResults.From(EnvelopeAssembler.Failure<DraftFacts>(session, new IssueItem("error", DraftArguments.InvalidCode, "'input' must be a JSON object of the draft's members (help topic=draft).", Location: null), cancellationToken));
        }

        // profile and asOf ride in the input object, so the menu lists two arguments; the help topic names them. Sent beside input,
        // each is moved into it, with a warning that says where it belongs.
        JsonObject members = JsonObject.Create(input)!;
        List<IssueItem> adjusted = [];
        foreach (string name in CallMembers)
        {
            if (context?.Params?.Arguments is { } sent && sent.TryGetValue(name, out JsonElement beside))
            {
                if (members.ContainsKey(name))
                {
                    return ToolResults.From(EnvelopeAssembler.Failure<DraftFacts>(session, new IssueItem("error", "invalid-parameter", $"'{name}' is given both beside 'input' and in it; give it once, in input (help topic=draft).", Location: null), cancellationToken));
                }

                members[name] = JsonNode.Parse(beside.GetRawText());
                adjusted.Add(new IssueItem("warning", LenientArguments.Code, $"'{name}' was given beside 'input' and was read as input.{name}, where draft_resource takes it (help topic=draft).", Location: null));
            }
        }

        string? profile;
        IReadOnlyList<string>? packages;
        string? asOf;
        try
        {
            profile = Take(members, "profile");
            packages = TakeList(members, "packages");
            asOf = Take(members, "asOf");
        }
        catch (ToolRequestException ex)
        {
            return ToolResults.From(EnvelopeAssembler.Failure<DraftFacts>(session, ex.Issue, cancellationToken));
        }

        var request = new DraftRequest(resourceType, members.ToJsonString(Unescaped), profile, packages, AsOf: asOf, CallMembers: CallMembers);
        ToolEnvelope<DraftFacts?> envelope = await DraftService.RunAsync(session, request, session.AsOfClock, loggerFactory.CreateLogger("draft_resource"), cancellationToken);
        return ToolResults.From(adjusted.Count == 0 ? envelope : envelope with { Issues = [.. adjusted, .. envelope.Issues] });
    }

    // The packages (id@version) taken out of the input: a list of text, or absent.
    private static IReadOnlyList<string>? TakeList(JsonObject members, string name)
    {
        if (!members.TryGetPropertyValue(name, out JsonNode? node))
        {
            return null;
        }

        if (node is not JsonArray items || items.Any(item => item is not JsonValue value || !value.TryGetValue(out string? _)))
        {
            throw DraftArguments.Invalid($"'input.{name}' must be a list of FHIR packages as id@version, such as [\"hl7.fhir.us.core@6.1.0\"].");
        }

        members.Remove(name);
        return [.. items.Select(item => item!.GetValue<string>())];
    }

    // A call member taken out of the input: text, or absent.
    private static string? Take(JsonObject members, string name)
    {
        if (!members.TryGetPropertyValue(name, out JsonNode? node))
        {
            return null;
        }

        if (node is not JsonValue value || !value.TryGetValue(out string? text))
        {
            throw DraftArguments.Invalid($"'input.{name}' must be text: {(name == "asOf" ? "a date or a date and time, such as 2024-05-01 or 2024-05-01T08:30:00Z" : "a profile canonical, url or url|version")}.");
        }

        members.Remove(name);
        return text;
    }
}
