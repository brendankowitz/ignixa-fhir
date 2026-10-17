// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.CodeSystems;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.Search.Parsing;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// For a search that matched nothing: the <c>code-system-not-in-folder</c> hint for each token of a plain token parameter of the
/// searched type (no modifier, no chain) whose system the type does not use or whose bare code no coding holds.
/// </summary>
internal static class SearchCodeHints
{
    private const int MostHints = 5;

    public static IEnumerable<IssueItem> ForEmptyResult(FolderSession session, string resourceType, ParsedSearch parsed, CancellationToken cancellationToken)
    {
        List<IssueItem> hints = [];
        foreach (ParameterTrace trace in parsed.Traces.Where(IsPlainTokenParameter))
        {
            CodedParameterUse use = CodedParameterUse.Read(session, resourceType, trace.Key);
            foreach (string token in trace.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(token => !token.Contains('\\', StringComparison.Ordinal)))
            {
                int bar = token.IndexOf('|', StringComparison.Ordinal);
                string? system = bar < 0 ? null : token[..bar];
                string code = bar < 0 ? token : token[(bar + 1)..];
                IssueItem? hint = bar >= 0 && system!.Length == 0
                    ? null
                    : UnusedCodeSystemHint.ForToken(session, resourceType, use, system, code, cancellationToken);
                if (hint is not null && !hints.Contains(hint) && hints.Count < MostHints)
                {
                    hints.Add(hint);
                }
            }
        }

        return hints;
    }

    private static bool IsPlainTokenParameter(ParameterTrace trace) =>
        trace.DataType == SearchParamType.Token
        && trace.Outcome is ParameterOutcome.Compiled
        && !trace.Key.StartsWith('_')
        && trace.Key.IndexOfAny([':', '.']) < 0;
}
