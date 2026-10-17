// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>A code the folder uses.</summary>
/// <param name="System">Its system.</param>
/// <param name="Code">The code.</param>
/// <param name="Resources">Resources carrying it, of any type.</param>
public sealed record FolderCodeUse(string System, string Code, int Resources);
