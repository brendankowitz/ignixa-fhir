// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>The part of an existing table that does not match its <see cref="FabricSilverContract"/>.</summary>
public enum FabricSilverTableAspect
{
    /// <summary>The Delta protocol is not exactly reader 1 / writer 4.</summary>
    Protocol,

    /// <summary>
    /// The schema differs from the contract's: any change to fields, their order, types, nullability or metadata.
    /// Whitespace and JSON member order are not differences.
    /// </summary>
    Schema,

    /// <summary>The table is partitioned.</summary>
    PartitionColumns,

    /// <summary><c>delta.enableChangeDataFeed</c> is missing or is not exactly lowercase <c>true</c>.</summary>
    ChangeDataFeed,

    /// <summary>A contract table property is missing or has a different value.</summary>
    Configuration,

    /// <summary>
    /// The table carries a <c>delta.*</c> property the contract does not declare and that is not known to leave
    /// writer obligations unchanged, such as a constraint, append-only or a table feature.
    /// </summary>
    UndeclaredProperty,

    /// <summary>The table directory holds files but no Delta log, so it is not a table this library can adopt.</summary>
    UnrecognizedContent,
}
