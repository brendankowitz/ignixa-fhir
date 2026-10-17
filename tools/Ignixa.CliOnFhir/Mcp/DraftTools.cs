// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Drafting;
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
    private static readonly IReadOnlySet<string> DraftArgumentNames = ToolArguments.Names(typeof(DraftTools).GetMethod(nameof(DraftResource))!);

    [McpServerTool(Name = "draft_resource", ReadOnly = true, Idempotent = true)]
    [Description("Checks a draft order or result. help: draft")]
    public async Task<CallToolResult> DraftResource(
        string resourceType,
        JsonElement input,
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

        // profile and asOf ride in the input object, so the menu, at its budget, lists two arguments; the help topic names them.
        JsonObject members = JsonObject.Create(input)!;
        string? profile = Take(members, "profile");
        string? asOf = Take(members, "asOf");
        var request = new DraftRequest(resourceType, members.ToJsonString(), profile, AsOf: asOf);
        return ToolResults.From(await DraftService.RunAsync(session, request, session.AsOfClock, loggerFactory.CreateLogger("draft_resource"), cancellationToken));
    }

    // A text member taken out of the input; any other kind is left in, for the draft's own strict check to refuse.
    private static string? Take(JsonObject members, string name)
    {
        if (members[name] is not JsonValue value || !value.TryGetValue(out string? text))
        {
            return null;
        }

        members.Remove(name);
        return text;
    }
}
