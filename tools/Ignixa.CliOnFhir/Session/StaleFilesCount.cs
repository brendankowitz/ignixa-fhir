// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Session;

/// <summary>The outcome of recounting which catalogued files have changed.</summary>
/// <param name="Count">Files modified, removed or added since the catalog was built; null when they could not be counted.</param>
/// <param name="Unavailable">Why the count could not be made (the folder was deleted or became unreadable); null when counted.</param>
public sealed record StaleFilesCount(int? Count, string? Unavailable);
