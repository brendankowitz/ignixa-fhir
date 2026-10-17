// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>A patient has a completed Immunization with one of the codes, in the window when there is one, else ever.</summary>
/// <param name="Codes">Vaccine codes, CVX or any system.</param>
/// <param name="Window">When it must have occurred; null for ever (at or before asOf).</param>
internal sealed record ImmunizationCriterion(IReadOnlyList<CohortCode> Codes, CohortWindow? Window);
