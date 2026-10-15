// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Catalog;

/// <summary>
/// A problem found while cataloguing or indexing the folder. Diagnostics are reported to the
/// caller, never dropped, so an agent can tell an empty result from data it could not read.
/// </summary>
/// <param name="Code">Stable machine-readable code, for example <c>invalid-json</c>.</param>
/// <param name="FilePath">Path relative to the catalog root.</param>
/// <param name="Line">1-based line for NDJSON input; null otherwise.</param>
/// <param name="Message">Human-readable detail.</param>
public sealed record CatalogDiagnostic(string Code, string FilePath, int? Line, string Message);
