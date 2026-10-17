// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>The coding inventory of a folder's catalogued resources, read once per catalog.</summary>
/// <param name="Systems">Per system, most codings first, then by URI.</param>
/// <param name="Implicit">FHIR <c>code</c> elements with a FHIR-defined binding, most values first.</param>
/// <param name="WithoutSystem">Codings that have a code but no system, per element.</param>
/// <param name="ResourcesRead">Resources whose JSON was read.</param>
/// <param name="Problems">Resources that could not be read (a changed or unreadable file), as warnings.</param>
/// <param name="ElapsedMilliseconds">Wall time of the walk.</param>
public sealed record CodeSystemInventory(
    IReadOnlyList<SystemTally> Systems,
    IReadOnlyList<ImplicitTally> Implicit,
    IReadOnlyList<ElementCount> WithoutSystem,
    int ResourcesRead,
    IReadOnlyList<IssueItem> Problems,
    long ElapsedMilliseconds);
