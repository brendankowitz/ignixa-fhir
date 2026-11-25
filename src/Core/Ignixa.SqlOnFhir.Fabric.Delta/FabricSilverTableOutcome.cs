// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>What <see cref="FabricSilverDeltaTables.EnsureTableAsync"/> did to make the table usable.</summary>
public enum FabricSilverTableOutcome
{
    /// <summary>This call published the table's version 0 commit.</summary>
    Created,

    /// <summary>
    /// The table already had a Delta log, or a concurrent creator published version 0 first, and its latest state
    /// matches the contract.
    /// </summary>
    Validated,
}
