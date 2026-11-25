// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>A <c>cdc</c> action and the commit version that wrote it.</summary>
internal sealed record DeltaLogChangeFile(long Version, JsonElement Action)
{
    /// <summary>The change data file path, relative to the table root as logged.</summary>
    public string Path => Action.GetProperty("path").GetString()!;
}
