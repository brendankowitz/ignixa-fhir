/*
 * Copyright (c) 2025, Ignixa Contributors
 */

namespace Ignixa.FhirPath.Types;

/// <summary>
/// What UCUM made of one unit string: its canonical dimension, and one of the unit in canonical units.
/// </summary>
/// <param name="Dimension">The canonical symbols; two units are commensurable exactly when these are equal.</param>
/// <param name="CanonicalOne">One of the unit, expressed in canonical units. Shared across threads and only read.</param>
internal sealed record UnitReading(string Dimension, Fhir.Metrics.Quantity CanonicalOne);
