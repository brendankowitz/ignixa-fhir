// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>One <c>compose.include.filter</c> / <c>exclude.filter</c>.</summary>
public sealed record ValueSetFilter(string? Property, string? Op, string? Value);
