// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Output;

/// <summary>One NDJSON file a run wrote.</summary>
/// <param name="ResourceType">The type of every resource in it.</param>
/// <param name="Path">The absolute path.</param>
/// <param name="Resources">Lines written, one resource each.</param>
/// <param name="Bytes">Size of the file.</param>
public sealed record NdjsonFileFacts(string ResourceType, string Path, long Resources, long Bytes);
