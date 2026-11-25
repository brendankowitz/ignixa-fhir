// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>The Delta types the writer can convert to Arrow: those a Silver projection can produce.</summary>
internal enum DeltaTypeKind
{
    String,
    Boolean,
    Byte,
    Short,
    Integer,
    Long,
    Decimal,
    Date,
    Timestamp,
    Struct,
    Array,
}
