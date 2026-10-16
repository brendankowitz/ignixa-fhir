// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Views;

/// <summary>The file a run wrote every row to.</summary>
/// <param name="Path">The absolute path.</param>
/// <param name="Format"><c>csv</c>, <c>ndjson</c> or <c>parquet</c>.</param>
/// <param name="Rows">Rows written.</param>
/// <param name="Bytes">Size of the file; 0 when none was created.</param>
/// <param name="Created">False when the view produced no rows, so no file was written.</param>
public sealed record ViewFileFacts(string Path, string Format, long Rows, long Bytes, bool Created);
