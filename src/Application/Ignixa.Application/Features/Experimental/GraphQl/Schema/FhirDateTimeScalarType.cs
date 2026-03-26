// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Experimental.GraphQl.Schema;

internal sealed class FhirDateTimeScalarType()
    : FhirStringScalarType("FhirDateTime", "FHIR dateTime scalar (ISO 8601 with FHIR partial date support)");
