// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>One concept of a loaded CodeSystem version.</summary>
/// <param name="Code">The code.</param>
/// <param name="Display">The display as written; null when the CodeSystem gives none.</param>
/// <param name="Definition">The definition as written; null when none.</param>
/// <param name="Status">The value of the property declared as <c>http://hl7.org/fhir/concept-properties#status</c> (or named <c>status</c>); null when none.</param>
/// <param name="Inactive">
/// True when the status is <c>retired</c> or <c>inactive</c>, or the property declared as <c>#inactive</c> (or named
/// <c>inactive</c>) is true. <c>deprecated</c> concepts are still active.
/// </param>
/// <param name="Properties">Every property in source order.</param>
/// <param name="Designations">Every designation in source order.</param>
/// <param name="Parents">Direct parents (from nesting and the parent, child and subsumedBy properties), in definition order.</param>
/// <param name="Children">Direct children, in definition order.</param>
/// <param name="Order">Position in the CodeSystem's definition order (depth first).</param>
public sealed record Concept(
    string Code,
    string? Display,
    string? Definition,
    string? Status,
    bool Inactive,
    IReadOnlyList<ConceptProperty> Properties,
    IReadOnlyList<ConceptDesignation> Designations,
    IReadOnlyList<string> Parents,
    IReadOnlyList<string> Children,
    int Order);
