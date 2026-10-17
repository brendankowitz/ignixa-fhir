// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>Facts of <c>folder_code_systems</c>.</summary>
/// <param name="Basis">What was counted, and how.</param>
/// <param name="ResourcesRead">Resources whose JSON was read.</param>
/// <param name="InventoryMilliseconds">Wall time of the walk (paid once per session).</param>
/// <param name="SystemCount">Distinct code systems.</param>
/// <param name="Offset">Systems skipped.</param>
/// <param name="Returned">Systems in this page.</param>
/// <param name="Top">Top codes listed per system.</param>
/// <param name="Systems">This page, most codings first, then by URI.</param>
/// <param name="ImplicitCodes">FHIR code elements with FHIR-defined bindings (at most 20), most values first.</param>
/// <param name="CodingsWithoutSystem">Codings with a code but no system, per element (at most 20).</param>
/// <param name="Terminology"><c>loaded</c> or <c>not loaded</c>: this tool never loads terminology itself unless asked (<c>withTerminology</c>).</param>
/// <param name="FhirCanonicalCheck">How <c>http://hl7.org/fhir/...</c> URIs were checked: against the loaded terminology, or by pattern only.</param>
public sealed record CodeSystemsFacts(
    string Basis,
    int ResourcesRead,
    long InventoryMilliseconds,
    int SystemCount,
    int Offset,
    int Returned,
    int Top,
    IReadOnlyList<CodeSystemUsage> Systems,
    IReadOnlyList<ImplicitCodeUsage> ImplicitCodes,
    IReadOnlyList<ElementCount> CodingsWithoutSystem,
    string Terminology,
    string FhirCanonicalCheck);
