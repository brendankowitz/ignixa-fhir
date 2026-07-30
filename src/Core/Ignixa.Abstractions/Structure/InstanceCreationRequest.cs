// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// A request to construct a FHIR object for a FHIRPath instance selector expression
/// (<c>Type { element: value, ... }</c>). Handed to the host-supplied instance-creation
/// delegate so the FHIRPath engine stays model-agnostic; the returned node should be the
/// same kind the engine navigates elsewhere (e.g. a schema-aware, source-node-backed element).
/// </summary>
/// <param name="TypeName">Unqualified type name (e.g. "Coding").</param>
/// <param name="NamespacePrefix">Optional namespace (e.g. "FHIR" from "FHIR.Coding"), or null.</param>
/// <param name="Elements">
/// Evaluated element assignments, in source order. Elements whose value expression evaluated
/// to an empty collection are already omitted per the FHIRPath spec.
/// </param>
public sealed record InstanceCreationRequest(
    string TypeName,
    string? NamespacePrefix,
    IReadOnlyList<InstanceElement> Elements);
