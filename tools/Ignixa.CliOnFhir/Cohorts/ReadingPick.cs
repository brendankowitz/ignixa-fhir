// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>Which of a patient's readings in the window an observation criterion compares.</summary>
internal enum ReadingPick
{
    /// <summary>The latest by instant; of two at the same instant, the smaller id.</summary>
    Latest,

    /// <summary>The earliest by instant; of two at the same instant, the smaller id.</summary>
    Earliest,

    /// <summary>Any reading: the patient meets the criterion when one of them does.</summary>
    Any,
}
