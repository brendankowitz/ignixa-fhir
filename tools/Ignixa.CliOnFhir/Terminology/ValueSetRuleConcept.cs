// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>A concept a ValueSet rule lists explicitly, with the display the ValueSet gives it (null when none).</summary>
public sealed record ValueSetRuleConcept(string Code, string? Display);
