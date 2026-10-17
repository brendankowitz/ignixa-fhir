// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>A code system in <c>folder_inspect</c>'s short summary.</summary>
public sealed record CodeSystemBrief(string System, string? Name, int DistinctCodes, int Codings);
