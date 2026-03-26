// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Experimental.GraphQl.Schema;

internal sealed class FhirTimeScalarType()
    : FhirStringScalarType("FhirTime", "FHIR time scalar (HH:MM:SS)");
