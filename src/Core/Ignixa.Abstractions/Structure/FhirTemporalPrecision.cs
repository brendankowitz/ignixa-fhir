// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// The precision a FHIR temporal literal was written at.
/// </summary>
/// <remarks>
/// Member ordering is load-bearing: callers select the coarser or finer of two precisions by
/// relational comparison (<c>left &gt;= right</c>), so members must stay ordered from coarsest to
/// finest and <see cref="Invalid"/> must stay at zero.
/// </remarks>
public enum FhirTemporalPrecision
{
    /// <summary>The literal is not a recognisable temporal value.</summary>
    Invalid,

    /// <summary>Year precision, e.g. <c>1974</c>.</summary>
    Year,

    /// <summary>Month precision, e.g. <c>1974-12</c>.</summary>
    Month,

    /// <summary>Day precision, e.g. <c>1974-12-25</c>.</summary>
    Day,

    /// <summary>Hour precision, e.g. <c>1974-12-25T14</c>.</summary>
    Hour,

    /// <summary>Minute precision, e.g. <c>1974-12-25T14:30</c>.</summary>
    Minute,

    /// <summary>Second precision, e.g. <c>1974-12-25T14:30:00</c>.</summary>
    Second,

    /// <summary>Millisecond precision, e.g. <c>1974-12-25T14:30:00.123</c>.</summary>
    Millisecond
}
