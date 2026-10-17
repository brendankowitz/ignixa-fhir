// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>The codes a criterion is searched with.</summary>
/// <param name="Codes">The codes listed, then those found through terminology, without repeats.</param>
/// <param name="Found">The codes found through a ValueSet or drug class that the folder uses, most used first; null when none was asked.</param>
internal sealed record CriterionCodes(IReadOnlyList<CohortCode> Codes, IReadOnlyList<CohortCode>? Found);
