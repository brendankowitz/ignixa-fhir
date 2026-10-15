// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Session;

/// <summary>Cost of building the catalog, measured in-process.</summary>
/// <param name="CatalogMilliseconds">Wall time to walk and sniff the folder.</param>
/// <param name="ManagedHeapBytes">Managed heap after a full collection, with the catalog alive.</param>
/// <param name="WorkingSetBytes">Process working set after the build.</param>
public sealed record BuildStats(long CatalogMilliseconds, long ManagedHeapBytes, long WorkingSetBytes);
