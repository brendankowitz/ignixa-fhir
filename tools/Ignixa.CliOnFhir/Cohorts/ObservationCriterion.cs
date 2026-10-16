// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>A patient's reading of one of the codes in the window, picked and compared with a value.</summary>
/// <param name="Codes">Observation codes (on Observation.code or a component's code).</param>
/// <param name="Window">When the reading must have been taken.</param>
/// <param name="Pick">Which reading is compared.</param>
/// <param name="Comparator">The comparison; null when only the existence of a reading is asked.</param>
/// <param name="Value">The value compared with; set exactly when <paramref name="Comparator"/> is.</param>
/// <param name="Unit">The unit <paramref name="Value"/> is in; a reading in another unit is not compared.</param>
/// <param name="IncludeMissing">A patient with no reading in the window meets the criterion.</param>
internal sealed record ObservationCriterion(
    IReadOnlyList<CohortCode> Codes,
    CohortWindow Window,
    ReadingPick Pick,
    ValueComparator? Comparator,
    decimal? Value,
    string? Unit,
    bool IncludeMissing);
