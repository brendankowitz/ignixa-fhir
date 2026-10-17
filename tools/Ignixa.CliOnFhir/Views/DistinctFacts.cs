// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Views;

/// <summary>The distinct values of one column over every row of the view.</summary>
/// <param name="Column">The column.</param>
/// <param name="Values">Distinct non-null values.</param>
/// <param name="Nulls">Rows whose value is null.</param>
public sealed record DistinctFacts(string Column, long Values, long Nulls);
