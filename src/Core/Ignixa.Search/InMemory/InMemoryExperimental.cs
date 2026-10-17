// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.InMemory;

/// <summary>
/// The diagnostic id of the experimental in-memory search API (<see cref="InMemorySearchEngine"/> and its companions).
/// Ignixa.Search is a stable package, so additions whose shape may still change are marked
/// <see cref="System.Diagnostics.CodeAnalysis.ExperimentalAttribute"/> and a consumer opts in by suppressing this id.
/// </summary>
internal static class InMemoryExperimental
{
    public const string DiagnosticId = "IGNIXAEXP001";
}
