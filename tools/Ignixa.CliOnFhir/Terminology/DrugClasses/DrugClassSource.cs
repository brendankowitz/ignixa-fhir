// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology.DrugClasses;

/// <summary>
/// One classification RxClass serves, and the requests that read it. ATC and VA relate classes to product-level RxNorm
/// codes (ATC through RxClass's ATCPROD source), which is what medication orders carry; EPC and MoA (FDA labels, through
/// DailyMed) relate them to ingredients only.
/// </summary>
/// <param name="Name">The value of the 'source' argument.</param>
/// <param name="ClassType">The RxClass class type allClasses lists.</param>
/// <param name="ClassesOf">The byRxcui parameters besides the RxCUI.</param>
/// <param name="Products">The classMembers parameters (besides classId) of each product-level relation.</param>
/// <param name="Ingredients">The classMembers parameters (besides classId) of the ingredient-level relation, if any.</param>
internal sealed record DrugClassSource(
    string Name,
    string ClassType,
    IReadOnlyList<(string Name, string Value)> ClassesOf,
    IReadOnlyList<IReadOnlyList<(string Name, string Value)>> Products,
    IReadOnlyList<(string Name, string Value)>? Ingredients)
{
    public static DrugClassSource Atc { get; } = new("ATC", "ATC1-4", [("relaSource", "ATC")], [[("relaSource", "ATCPROD")]], [("relaSource", "ATC")]);

    public static DrugClassSource Va { get; } = new("VA", "VA", [("relaSource", "VA")], [[("relaSource", "VA"), ("rela", "has_VAClass")], [("relaSource", "VA"), ("rela", "has_VAClass_extended")]], null);

    public static DrugClassSource Epc { get; } = new("EPC", "EPC", [("relaSource", "DAILYMED"), ("relas", "has_epc")], [], [("relaSource", "DAILYMED"), ("rela", "has_epc")]);

    public static DrugClassSource Moa { get; } = new("MOA", "MOA", [("relaSource", "DAILYMED"), ("relas", "has_moa")], [], [("relaSource", "DAILYMED"), ("rela", "has_moa")]);

    public static IReadOnlyList<DrugClassSource> All { get; } = [Atc, Va, Epc, Moa];

    public static string Names => string.Join(", ", All.Select(source => source.Name));

    public bool HasProducts => Products.Count > 0;

    /// <summary>The sources named by <paramref name="value"/> (comma-separated, any case), or <paramref name="defaults"/> when it names none.</summary>
    /// <exception cref="ToolRequestException">A name that is not a source.</exception>
    public static IReadOnlyList<DrugClassSource> Parse(string? value, IReadOnlyList<DrugClassSource> defaults)
    {
        List<DrugClassSource> sources = [];
        foreach (string name in (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            DrugClassSource source = All.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw ToolRequestException.Error("invalid-parameter", $"'source' takes {Names}, or several of them separated by commas.");
            if (!sources.Contains(source))
            {
                sources.Add(source);
            }
        }

        return sources.Count > 0 ? sources : defaults;
    }
}
