// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Validation;

/// <summary>One validation of resources in the folder.</summary>
/// <param name="ResourceType">Validate resources of this type: all of them, or those <paramref name="Query"/> matches.</param>
/// <param name="Query">A FHIR search query over <paramref name="ResourceType"/> (search syntax) choosing the resources to validate.</param>
/// <param name="Patient">
/// A Patient id (or digest): validate that patient's compartment (the Patient and every member), or, with
/// <paramref name="ResourceType"/>, the members of that type (matching <paramref name="Query"/>).
/// </param>
/// <param name="References">Validate exactly these resources (<c>Type/id</c>); not combined with the other scope arguments.</param>
/// <param name="Profile">Validate against this one profile (canonical url, optionally <c>|version</c>) and the core definition, instead of each resource's <c>meta.profile</c>.</param>
/// <param name="Packages">FHIR npm packages (<c>id@version</c>) whose StructureDefinitions and terminology to load first.</param>
/// <param name="Depth">
/// <c>full</c> (default: structure, cardinality, types, bindings, invariants, slicing) or <c>spec</c> (structure, cardinality,
/// types, required bindings), at which no resource is reported valid since invariants do not run.
/// </param>
/// <param name="Limit">Resources validated in this call, from <paramref name="Offset"/>; default and maximum depend on <paramref name="Detail"/>.</param>
/// <param name="Offset">Resources in scope to skip, in scope order.</param>
/// <param name="Detail">Also return each validated resource's full OperationOutcome (resource content).</param>
public sealed record ValidateRequest(
    string? ResourceType = null,
    string? Query = null,
    string? Patient = null,
    IReadOnlyList<string>? References = null,
    string? Profile = null,
    IReadOnlyList<string>? Packages = null,
    string? Depth = null,
    int? Limit = null,
    int Offset = 0,
    bool Detail = false);
