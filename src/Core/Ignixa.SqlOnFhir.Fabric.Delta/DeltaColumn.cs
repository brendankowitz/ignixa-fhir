// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// A Delta struct field read from a contract. Field metadata is not carried: the engine takes the table schema,
/// metadata included, from the Delta log, so the Arrow source of a merge needs only names, types and nullability.
/// </summary>
internal sealed record DeltaColumn(string Name, DeltaColumnType Type, bool Nullable)
{
    public Apache.Arrow.Field ToArrowField() => new(Name, Type.ToArrowType(), Nullable);
}
