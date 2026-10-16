// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>A code of an expansion that the folder uses.</summary>
/// <param name="System">Its system.</param>
/// <param name="Code">The code.</param>
/// <param name="Display">Its display in the expansion (terminology content).</param>
/// <param name="Resources">How many of the folder's resources carry it, of any type.</param>
public sealed record TermFolderCode(string System, string Code, string? Display, int Resources);
