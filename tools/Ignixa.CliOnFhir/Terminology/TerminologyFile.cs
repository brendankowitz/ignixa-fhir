// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>One JSON resource handed to <see cref="TerminologySourceLoader"/>.</summary>
/// <param name="Path">Path within the package, or relative to the folder.</param>
/// <param name="Json">The resource's UTF-8 JSON.</param>
/// <param name="Line">1-based NDJSON line in the folder; null otherwise.</param>
/// <param name="Offset">Byte offset of the resource within its file.</param>
public sealed record TerminologyFile(string Path, ReadOnlyMemory<byte> Json, int? Line, long Offset);
