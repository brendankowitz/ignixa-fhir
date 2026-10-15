// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>A resource of a patient's context with its summary item and the catalogued resources the item was read from.</summary>
/// <param name="Resource">The resource the item describes (for a lab code, its latest observation).</param>
/// <param name="Item">The summary.</param>
/// <param name="Sources">The resource and any it drew values from (a referenced Medication), for evidence.</param>
/// <param name="Absent">Resources the item needed and the folder does not hold (a referenced Medication), for <c>absent</c>.</param>
/// <param name="Date">The main clinical date the item shows (<see cref="ClinicalRules.MainDate"/>), which orders and windows it; null when it has none.</param>
internal sealed record ContextEntry(IndexedResource Resource, ContextItem Item, IReadOnlyList<CatalogEntry> Sources, IReadOnlyList<AbsentItem> Absent, FhirTemporal? Date);
