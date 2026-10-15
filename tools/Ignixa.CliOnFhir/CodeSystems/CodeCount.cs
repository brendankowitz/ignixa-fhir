// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>A code and how often it occurs; <paramref name="Code"/> is <c>system|code</c> for a code system's codes, the bare code for implicit ones.</summary>
public sealed record CodeCount(string Code, int Count);
