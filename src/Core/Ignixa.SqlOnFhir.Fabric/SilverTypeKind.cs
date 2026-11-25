// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>The Delta column types a Silver row can hold. Map, binary, float and double columns are not projected.</summary>
internal enum SilverTypeKind
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
