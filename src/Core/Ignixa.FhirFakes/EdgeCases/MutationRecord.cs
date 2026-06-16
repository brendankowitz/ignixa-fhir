// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// A self-describing record of a single applied mutation, suitable for serialization into a manifest.
/// </summary>
/// <param name="Category">The hierarchical category of the strategy that produced this mutation (e.g. "unicode.rtl").</param>
/// <param name="Path">The JSON path to the mutated leaf (e.g. "name[0].family").</param>
/// <param name="Before">The value before mutation.</param>
/// <param name="After">The value after mutation.</param>
public sealed record MutationRecord(string Category, string Path, string? Before, string? After);
