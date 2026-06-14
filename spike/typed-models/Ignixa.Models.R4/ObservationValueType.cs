// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

// CA1720: 'String' is a deliberate FHIR variant name (valueString -> string). A real generator
// emitting choice discriminators from StructureDefinitions hits this for every primitive whose
// FHIR type name collides with a CLR type (string/object/single/...). Suppressed here rather than
// renamed so the discriminator mirrors the FHIR type names callers expect.
#pragma warning disable CA1720

namespace Ignixa.Models.R4;

/// <summary>
/// Discriminator for the <c>Observation.value[x]</c> choice element: which typed variant
/// (if any) is currently present in the JSON.
/// </summary>
public enum ObservationValueType
{
    None,
    Quantity,
    String,
    CodeableConcept,
}
