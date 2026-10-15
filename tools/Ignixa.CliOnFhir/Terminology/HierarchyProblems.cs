// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>Defects found while reading a CodeSystem's concepts; the affected concepts or edges are left out and answers say so.</summary>
/// <param name="DanglingEdges">Parent or child references to codes the CodeSystem does not define.</param>
/// <param name="CycleEdges">Parent edges that would make a concept its own ancestor.</param>
/// <param name="DuplicateCodes">Concepts repeating an earlier code (the first definition is kept).</param>
public sealed record HierarchyProblems(int DanglingEdges, int CycleEdges, int DuplicateCodes)
{
    public static HierarchyProblems None { get; } = new(0, 0, 0);

    public bool Any => DanglingEdges > 0 || CycleEdges > 0 || DuplicateCodes > 0;
}
