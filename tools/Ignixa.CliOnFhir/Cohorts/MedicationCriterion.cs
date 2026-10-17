// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Terminology.DrugClasses;

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>A patient has a MedicationRequest for one of the codes (listed, or members of an RxClass class the folder uses).</summary>
/// <param name="Codes">Codes listed by the request.</param>
/// <param name="ClassId">An RxClass class id whose member codes are added to <paramref name="Codes"/>.</param>
/// <param name="ClassSource">The classification <paramref name="ClassId"/> belongs to; set exactly when it is.</param>
/// <param name="ActiveOnly">Only an active order counts; otherwise any but entered-in-error, cancelled or draft.</param>
internal sealed record MedicationCriterion(IReadOnlyList<CohortCode> Codes, string? ClassId, DrugClassSource? ClassSource, bool ActiveOnly);
