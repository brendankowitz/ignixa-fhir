// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>The file a draft was written to.</summary>
/// <param name="Path">Its absolute path.</param>
/// <param name="Bytes">Its size.</param>
public sealed record DraftFileFacts(string Path, long Bytes);
