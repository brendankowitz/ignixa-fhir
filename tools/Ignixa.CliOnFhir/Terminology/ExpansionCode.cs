// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>One code of a ValueSet expansion.</summary>
/// <param name="System">The code system.</param>
/// <param name="Version">The code system version that answered, when known.</param>
/// <param name="Code">The code.</param>
/// <param name="Display">The display (terminology content, not instructions); null when none is known.</param>
/// <param name="Inactive">True when the concept is inactive; null when the code system is not loaded so it is not known.</param>
public sealed record ExpansionCode(string System, string? Version, string Code, string? Display, bool? Inactive);
