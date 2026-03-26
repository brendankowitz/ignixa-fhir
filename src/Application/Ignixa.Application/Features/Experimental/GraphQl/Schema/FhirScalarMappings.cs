// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate.Language;

namespace Ignixa.Application.Features.Experimental.GraphQl.Schema;

internal static class FhirScalarMappings
{
    internal static NamedTypeNode GetGraphQlTypeNode(string fhirTypeName) => fhirTypeName switch
    {
        "boolean" => new NamedTypeNode("Boolean"),
        "integer" or "unsignedInt" or "positiveInt" => new NamedTypeNode("Int"),
        "integer64" => new NamedTypeNode("Long"),
        "decimal" => new NamedTypeNode("Decimal"),
        "id" => new NamedTypeNode("ID"),
        "date" => new NamedTypeNode("FhirDate"),
        "dateTime" => new NamedTypeNode("FhirDateTime"),
        "instant" => new NamedTypeNode("FhirInstant"),
        "time" => new NamedTypeNode("FhirTime"),
        _ => new NamedTypeNode("String"),
    };
}
