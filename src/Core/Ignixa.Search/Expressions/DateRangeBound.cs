// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Expressions;

/// <summary>
/// Identifies which end of an indexed date range a comparison applies to.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="FieldName"/>: this exists so the FHIR prefix table can be stated once,
/// in terms of the interval itself, without committing to how any one backend spells its columns.
/// </remarks>
public enum DateRangeBound
{
    /// <summary>
    /// The lower bound of the indexed value's range.
    /// </summary>
    Start,

    /// <summary>
    /// The upper bound of the indexed value's range.
    /// </summary>
    End,
}
