// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Session;

/// <summary>Cost of building the catalog and index, measured in-process.</summary>
/// <param name="CatalogMilliseconds">Wall time to walk and sniff the folder.</param>
/// <param name="IndexMilliseconds">Wall time to parse and index every catalogued resource.</param>
/// <param name="ManagedHeapBytes">Managed heap after a full collection, with the index alive.</param>
/// <param name="WorkingSetBytes">Process working set after the build.</param>
public sealed record BuildStats(long CatalogMilliseconds, long IndexMilliseconds, long ManagedHeapBytes, long WorkingSetBytes);
