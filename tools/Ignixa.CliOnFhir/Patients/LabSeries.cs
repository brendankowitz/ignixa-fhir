// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Patients;

/// <summary>A patient's observations of one lab code, newest first, and the item summarizing them.</summary>
/// <param name="NodeKey">The series' node key.</param>
/// <param name="Summary">The latest observation's entry, its item carrying the count and trend.</param>
/// <param name="Observations">Every observation of the code in the window, newest first.</param>
internal sealed record LabSeries(string NodeKey, ContextEntry Summary, IReadOnlyList<ContextEntry> Observations);
