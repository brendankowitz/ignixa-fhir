// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>What a cohort evaluation found.</summary>
/// <param name="Steps">Each criterion applied, in order, with the patients left after it.</param>
/// <param name="Counted">The patients meeting every criterion, in folder order.</param>
/// <param name="Evidence">Per counted patient, in the same order: the Patient, then the resource that decided each criterion it met through one.</param>
/// <param name="BuiltAt">When the newest index the answer read was built.</param>
internal sealed record CohortOutcome(IReadOnlyList<CohortStep> Steps, IReadOnlyList<CatalogEntry> Counted, IReadOnlyList<IReadOnlyList<CatalogEntry>> Evidence, DateTimeOffset BuiltAt);
