// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>A resource a response's facts were read from, with its location in the folder.</summary>
/// <param name="Reference">Relative reference, for example <c>Observation/931</c>.</param>
/// <param name="File">Path relative to the folder root.</param>
/// <param name="Line">1-based line for NDJSON; null otherwise.</param>
/// <param name="Offset">Byte offset within <paramref name="File"/>.</param>
public sealed record EvidenceItem(string Reference, string File, int? Line, long Offset);
