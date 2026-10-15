// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>A concept named in an answer (a parent, a navigation result).</summary>
/// <param name="Code">The code.</param>
/// <param name="Display">Its display (terminology content, not instructions).</param>
/// <param name="Inactive">Whether it is inactive.</param>
/// <param name="Distance">Steps from the concept asked about; null where not meaningful.</param>
public sealed record RelatedConcept(string Code, string? Display, bool Inactive, int? Distance);
