// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Marks an <see cref="IValidationCheck"/> as per-resource rather than per-element: it must run at
/// most once per composed schema. When <see cref="ValidationSchema.Compose"/> merges multiple schemas
/// (e.g. a base resource schema plus declared profiles), checks carrying this marker are deduplicated
/// by concrete type and the <b>first occurrence wins</b>; later instances of the same type are dropped.
/// <para>
/// Use this for stateless whole-resource checks (JSON shape, narrative, resourceType validity) and for
/// checks whose first schema already covers the resource (e.g. unknown-property detection keyed on the
/// base StructureDefinition). Parameterized per-element checks (cardinality, type, binding) must NOT
/// carry this marker — each instance encodes distinct element metadata and they all need to run.
/// </para>
/// </summary>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface: carries the run-once-per-composed-schema contract for ValidationSchema.Compose dedup.")]
public interface ISingletonCheck
{
}
