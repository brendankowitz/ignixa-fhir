// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.CodeSystems;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// How the folder's type indexes populate search parameters: per type and parameter, the resources with at least one index
/// entry, and for a token parameter its most common <c>system|code</c> values by resources. Values come from the data, so a
/// system that is not an http(s) or urn URI, or a code that is not shaped as one, is shown as its digest, as
/// <c>folder_code_systems</c> shows them; displays are never read. A type's index is built (or loaded) on first use, through
/// <see cref="Source"/>, so its diagnostics can be reported. For one call; not thread-safe.
/// </summary>
internal sealed class ParameterPopulation(FolderSession session)
{
    public const int MaxTopValues = 10;

    private readonly Dictionary<(string Type, Uri Url), (int Populated, IReadOnlyList<TopValue>? Top)> _counts = [];

    /// <summary>The indexes read so far.</summary>
    public FolderSearchSource Source { get; } = new(session);

    /// <summary>Distinct top values shown as digests, because the data's system or code was not shaped as one.</summary>
    public int DigestedValues { get; private set; }

    /// <summary>Resources of <paramref name="resourceType"/> in its index; 0, without reading an index, when the folder holds none.</summary>
    public int Indexed(string resourceType) => session.HoldsType(resourceType) ? Source.Resources(resourceType).Count : 0;

    /// <summary>How <paramref name="parameter"/> is populated on <paramref name="resourceType"/>.</summary>
    public (int Populated, IReadOnlyList<TopValue>? TopValues) Count(string resourceType, SearchParameterInfo parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        if (_counts.TryGetValue((resourceType, parameter.Url), out (int, IReadOnlyList<TopValue>?) known))
        {
            return known;
        }

        IReadOnlyList<IndexedResource> resources = session.HoldsType(resourceType) ? Source.Resources(resourceType) : [];
        (int, IReadOnlyList<TopValue>?) counted = parameter.Code == "_id"
            ? (resources.Count, null) // Every resource has its id; the engine matches _id against the resource key, not an entry.
            : Tally(resources, parameter);
        _counts[(resourceType, parameter.Url)] = counted;
        return counted;
    }

    private (int Populated, IReadOnlyList<TopValue>? Top) Tally(IReadOnlyList<IndexedResource> resources, SearchParameterInfo parameter)
    {
        bool token = parameter.Type == Ignixa.Specification.ValueSets.Normative.SearchParamType.Token;
        int populated = 0;
        Dictionary<string, int> values = new(StringComparer.Ordinal);
        HashSet<string> digested = new(StringComparer.Ordinal);
        foreach (IndexedResource resource in resources)
        {
            List<SearchIndexEntry> entries = [.. resource.Index.Where(entry => entry.SearchParameter.Url == parameter.Url)];
            if (entries.Count == 0)
            {
                continue;
            }

            populated++;
            if (token)
            {
                foreach ((string value, bool changed) in entries.Select(entry => entry.Value).OfType<TokenSearchValue>().Where(value => value.Code is not null).Select(Shown).Distinct())
                {
                    values[value] = values.GetValueOrDefault(value) + 1;
                    if (changed)
                    {
                        digested.Add(value);
                    }
                }
            }
        }

        if (!token)
        {
            return (populated, null);
        }

        List<TopValue> top = [.. values
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(MaxTopValues)
            .Select(pair => new TopValue(pair.Key, pair.Value))];
        DigestedValues += top.Count(value => digested.Contains(value.Value));
        return (populated, top);
    }

    /// <summary><c>system|code</c> or <c>|code</c>, each part shape-checked as <c>folder_code_systems</c> checks it, and whether either was replaced.</summary>
    private static (string Value, bool Digested) Shown(TokenSearchValue value)
    {
        string system = value.System is null ? string.Empty : FolderCodeSystems.SafeSystem(value.System);
        string code = TerminologyNames.Code(value.Code!);
        return ($"{system}|{code}", (value.System is not null && system != value.System) || code != value.Code);
    }
}
