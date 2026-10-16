// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using System.Reflection;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>
/// Arguments a client sent that the tool does not define. The MCP SDK binds the arguments it knows and drops the rest without a
/// trace, so <c>"_sort": "-date"</c> passed beside <c>query</c> would silently not sort.
/// </summary>
internal static class ToolArguments
{
    /// <summary>The argument names of a tool method, as the client sends them; injected parameters are not arguments.</summary>
    public static IReadOnlySet<string> Names(MethodInfo method) =>
        method.GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken) && parameter.ParameterType != typeof(RequestContext<CallToolRequestParams>))
            .Select(parameter => parameter.Name!)
            .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Runs <paramref name="call"/> when every sent argument is one the tool defines. Otherwise the call fails with
    /// <c>invalid-parameter</c> naming them, or, when <paramref name="lenient"/>, runs with a <c>parameter-ignored</c> warning.
    /// </summary>
    public static ToolEnvelope<TFacts?> Check<TFacts>(
        FolderSession session,
        IReadOnlySet<string> known,
        IEnumerable<string>? sent,
        bool lenient,
        Func<ToolEnvelope<TFacts?>> call,
        CancellationToken cancellationToken)
        where TFacts : class
    {
        List<string> unknown = [.. (sent ?? []).Where(name => !known.Contains(name))];
        if (unknown.Count == 0)
        {
            return call();
        }

        string named = string.Join(", ", unknown.Take(5).Select(name => $"'{(name.Length > 64 ? name[..64] + "..." : name)}'"));
        string problem = $"{named} {(unknown.Count == 1 ? "is not an argument" : "are not arguments")} of this tool. Every FHIR search parameter, _sort and _count included, goes inside 'query', for example query='code=http://loinc.org|4548-4&_sort=-date'. Its arguments are {string.Join(", ", known.Order(StringComparer.Ordinal))}.";
        if (!lenient)
        {
            return EnvelopeAssembler.Failure<TFacts>(session, new IssueItem("error", "invalid-parameter", problem, Location: null), cancellationToken);
        }

        ToolEnvelope<TFacts?> envelope = call();
        return envelope with { Issues = [.. envelope.Issues, new IssueItem("warning", "parameter-ignored", $"{problem} Ignored because lenient is true.", Location: null)] };
    }
}
