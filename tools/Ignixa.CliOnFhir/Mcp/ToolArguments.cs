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
/// Arguments a client sent that the tool does not define, or that the chosen operation does not take. The MCP SDK binds the
/// arguments it knows and drops the rest without a trace, so <c>"_sort": "-date"</c> passed beside <c>query</c> would
/// silently not sort, and a <c>valueSet</c> sent with <c>operation=lookup</c> would silently not narrow anything.
/// </summary>
internal static class ToolArguments
{
    /// <summary>The argument names of a tool method, as the client sends them; injected parameters are not arguments.</summary>
    public static IReadOnlySet<string> Names(MethodInfo method) =>
        method.GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken) && parameter.ParameterType != typeof(RequestContext<CallToolRequestParams>))
            .Select(parameter => parameter.Name!)
            .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The names in <paramref name="sent"/> that <paramref name="known"/> lacks, as an error message; null when there are none.</summary>
    public static string? Unknown(IReadOnlySet<string> known, IEnumerable<string>? sent)
    {
        List<string> unknown = [.. (sent ?? []).Where(name => !known.Contains(name))];
        return unknown.Count == 0
            ? null
            : $"{Quote(unknown)} {(unknown.Count == 1 ? "is not an argument" : "are not arguments")} of this tool. Its arguments are {string.Join(", ", known.Order(StringComparer.Ordinal))}.";
    }

    /// <summary>
    /// Runs <paramref name="call"/> when every sent argument is one the tool defines. Otherwise the call fails with
    /// <c>invalid-parameter</c> naming them and saying that search parameters go inside <c>query</c>, or, when
    /// <paramref name="lenient"/>, runs with a <c>parameter-ignored</c> warning.
    /// </summary>
    public static ToolEnvelope<TFacts?> CheckSearch<TFacts>(
        FolderSession session,
        IReadOnlySet<string> known,
        IEnumerable<string>? sent,
        bool lenient,
        Func<ToolEnvelope<TFacts?>> call,
        CancellationToken cancellationToken)
        where TFacts : class
    {
        if (Unknown(known, sent) is not { } unknown)
        {
            return call();
        }

        string problem = $"{unknown} Every FHIR search parameter, _sort and _count included, goes inside 'query', for example query='code=http://loinc.org|4548-4&_sort=-date'.";
        if (!lenient)
        {
            return EnvelopeAssembler.Failure<TFacts>(session, Invalid(problem), cancellationToken);
        }

        ToolEnvelope<TFacts?> envelope = call();
        return envelope with { Issues = [.. envelope.Issues, new IssueItem("warning", "parameter-ignored", $"{problem} Ignored because lenient is true.", Location: null)] };
    }

    /// <summary>
    /// Why the arguments of a call to a tool with an <c>operation</c> argument cannot run: a name the tool does not define, an
    /// operation it does not have, a required argument missing or an argument the operation does not take. Null when they can.
    /// </summary>
    /// <param name="known">Every argument name of the tool.</param>
    /// <param name="sent">The names the client sent, as it sent them (null when the call did not come over MCP).</param>
    /// <param name="operations">The tool's operations.</param>
    /// <param name="operation">The operation asked for.</param>
    /// <param name="given">The names of the arguments that have a value, <c>operation</c> excluded.</param>
    public static IssueItem? CheckOperation(
        IReadOnlySet<string> known,
        IEnumerable<string>? sent,
        IReadOnlyList<ToolOperation> operations,
        string? operation,
        IEnumerable<string> given)
    {
        if (Unknown(known, sent) is { } unknown)
        {
            return Invalid(unknown);
        }

        string all = string.Join(" | ", operations.Select(candidate => candidate.Name));
        if (string.IsNullOrWhiteSpace(operation))
        {
            return Invalid($"'operation' is required: {all}.");
        }

        if (operations.FirstOrDefault(candidate => candidate.Name == operation) is not { } chosen)
        {
            return Invalid($"{Quote([operation])} is not an operation of this tool: {all}.");
        }

        List<string> provided = [.. given];
        List<string> missing = [.. chosen.Required.Where(name => !provided.Contains(name, StringComparer.Ordinal))];
        if (missing.Count > 0)
        {
            return Invalid($"operation={chosen.Name} needs {Quote(missing)}. It takes {chosen.Signature}.");
        }

        List<string> foreign = [.. provided.Where(name => !chosen.Takes(name))];
        return foreign.Count == 0
            ? null
            : Invalid($"operation={chosen.Name} does not take {Quote(foreign)}. It takes {chosen.Signature}.");
    }

    /// <summary>The names of the arguments that have a value: not null, and for text not blank.</summary>
    public static IEnumerable<string> Given(params (string Name, object? Value)[] arguments) =>
        arguments
            .Where(argument => argument.Value switch { null => false, string text => !string.IsNullOrWhiteSpace(text), _ => true })
            .Select(argument => argument.Name);

    private static IssueItem Invalid(string problem) => new("error", "invalid-parameter", problem, Location: null);

    private static string Quote(IEnumerable<string> names) =>
        string.Join(", ", names.Take(5).Select(name => $"'{(name.Length > 64 ? name[..64] + "..." : name)}'"));
}
