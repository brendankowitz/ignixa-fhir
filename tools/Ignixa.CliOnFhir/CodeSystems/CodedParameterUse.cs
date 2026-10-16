// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology.Tools;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>
/// The codings one token search parameter of one resource type holds in the folder's search index, which is what a search of
/// that parameter sees: the systems used (most codings first) and every coding. A parameter over Identifier elements is not
/// coded and reads as having none.
/// </summary>
internal sealed class CodedParameterUse
{
    private readonly Dictionary<string, int> _systemCodings = new(StringComparer.Ordinal);
    private readonly HashSet<(string? System, string Code)> _codings = [];
    private readonly HashSet<string> _codes = new(StringComparer.Ordinal);

    private CodedParameterUse()
    {
    }

    /// <summary>The FHIRPath expression of the search parameter, as the first indexed coding's parameter states it; null when none is indexed.</summary>
    public string? Expression { get; private set; }

    /// <summary>Whether any resource holds a coding with a system in the parameter; otherwise it is not a coded parameter.</summary>
    public bool IsCoded => _systemCodings.Count > 0;

    /// <summary>The systems the parameter's codings use, most codings first, then by URI.</summary>
    public IReadOnlyList<string> Systems => [.. _systemCodings.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key)];

    public bool UsesSystem(string system) => _systemCodings.ContainsKey(system);

    /// <summary>Whether some coding has <paramref name="code"/> in any system.</summary>
    public bool HasCode(string code) => _codes.Contains(code);

    /// <summary>Whether a coding has exactly <paramref name="code"/>, in <paramref name="system"/> when one is given and in any system otherwise.</summary>
    public bool Has(string? system, string code) => system is null ? _codes.Contains(code) : _codings.Contains((system, code));

    /// <summary>Reads the index of <paramref name="resourceType"/>; a type the folder does not hold has no codings.</summary>
    public static CodedParameterUse Read(FolderSession session, string resourceType, string parameter)
    {
        ArgumentNullException.ThrowIfNull(session);

        var use = new CodedParameterUse();
        if (!session.HoldsType(resourceType))
        {
            return use;
        }

        foreach (IndexedResource resource in session.GetIndex(resourceType).Resources)
        {
            foreach (var entry in resource.Index)
            {
                if (entry.SearchParameter.Code != parameter || FolderTerminology.OverIdentifier(entry.SearchParameter) || entry.Value is not TokenSearchValue { Code: { Length: > 0 } code } token)
                {
                    continue;
                }

                use.Expression ??= entry.SearchParameter.Expression;
                use._codes.Add(code);
                string? system = token.System is { Length: > 0 } written ? written : null;
                if (use._codings.Add((system, code)) && system is not null)
                {
                    use._systemCodings[system] = use._systemCodings.GetValueOrDefault(system) + 1;
                }
            }
        }

        return use;
    }
}
