// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Models;

/// <summary>
/// Represents the parsed configuration for the Observation <c>$lastn</c> operation.
/// </summary>
public sealed record LastNSearchOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LastNSearchOptions"/> class.
    /// </summary>
    /// <param name="filters">The ordinary Observation filters that select the candidate set.</param>
    /// <param name="maximum">The maximum number of effective-time ranks to retain per code group.</param>
    /// <param name="codeParameter">The version-specific Observation code search parameter.</param>
    /// <param name="effectiveDateParameter">The version-specific Observation effective-date search parameter.</param>
    /// <param name="countSpecified">Whether the request explicitly supplied the unsupported ordinary <c>_count</c> control.</param>
    /// <param name="continuationSpecified">Whether the request explicitly supplied an unsupported continuation control.</param>
    public LastNSearchOptions(
        SearchOptions filters,
        int maximum,
        SearchParameterInfo codeParameter,
        SearchParameterInfo effectiveDateParameter,
        bool countSpecified = false,
        bool continuationSpecified = false)
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        ArgumentNullException.ThrowIfNull(codeParameter);
        ArgumentNullException.ThrowIfNull(effectiveDateParameter);

        Filters = filters;
        Maximum = maximum;
        CodeParameter = codeParameter;
        EffectiveDateParameter = effectiveDateParameter;
        CountSpecified = countSpecified;
        ContinuationSpecified = continuationSpecified;
    }

    /// <summary>Gets the ordinary Observation filters that select the candidate set.</summary>
    public SearchOptions Filters { get; }

    /// <summary>Gets the maximum number of effective-time ranks to retain per code group.</summary>
    public int Maximum { get; }

    /// <summary>Gets the version-specific Observation code search parameter.</summary>
    public SearchParameterInfo CodeParameter { get; }

    /// <summary>Gets the version-specific Observation effective-date search parameter.</summary>
    public SearchParameterInfo EffectiveDateParameter { get; }

    /// <summary>Gets whether the request explicitly supplied the ordinary <c>_count</c> control.</summary>
    public bool CountSpecified { get; }

    /// <summary>Gets whether the request explicitly supplied an ordinary continuation control.</summary>
    public bool ContinuationSpecified { get; }
}
