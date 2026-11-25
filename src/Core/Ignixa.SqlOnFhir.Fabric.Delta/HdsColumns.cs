// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>The Silver columns the writer's merge semantics depend on.</summary>
internal static class HdsColumns
{
    public const string Id = "id";
    public const string MetaLastUpdated = "meta_lastUpdated";
    public const string CreatedDatetime = "msftCreatedDatetime";
}
