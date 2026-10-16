// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Views;

/// <summary>One row of a view.</summary>
/// <param name="Cells">One value per output column, in column order: null, text, a boolean or a number.</param>
/// <param name="Source">The resource the row was made from; null when the engine orders rows across resources (<see cref="CompiledView.OrdersAcrossResources"/>).</param>
internal sealed record ViewRow(object?[] Cells, CatalogEntry? Source);
