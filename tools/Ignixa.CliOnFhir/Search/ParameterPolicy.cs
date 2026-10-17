// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using Ignixa.Search.Parsing;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// The single place that decides which query parameters the tool may leave unapplied. A parameter that changes which
/// resources match, or which page is returned, must never be dropped with only a warning, since the results would look
/// filtered when they are not. Rule, in order:
/// <list type="number">
/// <item>a name in <see cref="UnappliedFilters"/>, a continuation token, or a <c>_type</c> that names another type fails the request;</item>
/// <item>a name in <see cref="ResultParameters"/> (or <c>_type</c> naming the searched type) is ignored with a <c>parameter-ignored</c> warning;</item>
/// <item>any other name the search pipeline does not recognise fails the request as <c>unknown-search-parameter</c> with a
/// "did you mean" hint, or, in lenient mode, is ignored with a <c>parameter-ignored</c> warning, as FHIR lets a server do for
/// unknown parameters (<c>handling=lenient</c>).</item>
/// </list>
/// <c>_sort</c>, <c>_include</c>, <c>_revinclude</c>, chains and <c>_has</c> are evaluated by Ignixa's in-memory search engine.
/// </summary>
internal static class ParameterPolicy
{
    /// <summary>Standard FHIR filtering parameters this tool does not evaluate; using one fails with <c>search-not-supported</c>.</summary>
    public static readonly FrozenSet<string> UnappliedFilters = new[]
    {
        "_text",
        "_content",
        "_query",
        "_list",
        "_filter",
        "_not-referenced",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Result-shaping parameters that do not change which resources match; ignored with a warning.</summary>
    public static readonly FrozenSet<string> ResultParameters = new[]
    {
        "_format",
        "_pretty",
        "_contained",
        "_containedType",
        "_includesCount",
        "_includesContinuationToken",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Names a search accepts besides the type's search parameter codes, offered when suggesting a name for an unknown one.
    /// </summary>
    public static readonly FrozenSet<string> ControlParameters = new[]
    {
        "_count",
        "_elements",
        "_include",
        "_revinclude",
        "_sort",
        "_summary",
        "_total",
        "_type",
    }.Concat(ResultParameters).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The parameter name without its modifier or chain.</summary>
    public static string BaseName(string name)
    {
        int end = name.AsSpan().IndexOfAny(':', '.');
        return end < 0 ? name : name[..end];
    }

    /// <summary>Why <paramref name="parameter"/> cannot be left unapplied, or null when it may be.</summary>
    public static string? Rejection(QueryParameter parameter, string resourceType)
    {
        string baseName = BaseName(parameter.Name);
        if (UnappliedFilters.Contains(baseName))
        {
            return $"{baseName} is a filter this tool does not evaluate";
        }

        if (parameter.Category == ParameterCategory.ContinuationToken)
        {
            return "continuation tokens are not used; page with offset instead";
        }

        if (baseName == "_type" && (parameter.Name != "_type" || !NamesOnly(parameter.Value, resourceType)))
        {
            return $"_type is only accepted as the searched type '{resourceType}', since searches are per resource type";
        }

        return null;
    }

    private static bool NamesOnly(string value, string resourceType) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(type => type == resourceType);
}
