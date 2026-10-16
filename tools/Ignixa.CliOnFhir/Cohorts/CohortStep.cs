// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>One criterion, applied in order, and how many patients met it and every criterion before it.</summary>
/// <param name="Criterion">Where the request has it: patients, living, age, gender, conditions[0], medications[0], ...</param>
/// <param name="Rule">The rule applied, with its codes counted and its window; fixed text and request values only.</param>
/// <param name="Remaining">Patients left after this criterion.</param>
public sealed record CohortStep(string Criterion, string Rule, int Remaining)
{
    /// <summary>Most codes a step lists.</summary>
    public const int CodesListed = 10;

    /// <summary>For a ValueSet or drug class: the codes the folder uses that were searched, at most <see cref="CodesListed"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Codes { get; init; }

    /// <summary>For a ValueSet or drug class: how many codes were searched in all (listed and given).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CodeCount { get; init; }

    /// <summary>For an observation with missing=include: how many of <see cref="Remaining"/> had no reading in the window.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WithoutReading { get; init; }
}
