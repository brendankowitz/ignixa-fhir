// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.SourceNodeSerialization;
using Ignixa.Specification.Generated;

namespace Ignixa.Specification.Extensions;

/// <summary>
/// Extension methods for working with IFhirSchemaProvider instances based on FhirSpecification.
/// </summary>
public static class FhirSpecificationSchemaProviderExtensions
{
    /// <summary>
    /// Gets the appropriate IFhirSchemaProvider instance for the given FhirSpecification.
    /// Creates and returns the correct provider based on the FHIR version.
    /// </summary>
    /// <param name="spec">The FHIR specification enum value.</param>
    /// <returns>The schema provider instance for the specified version.</returns>
    /// <exception cref="NotSupportedException">Thrown if the FHIR specification version is not supported.</exception>
    public static IFhirSchemaProvider GetSchemaProvider(this FhirSpecification spec)
    {
        return spec switch
        {
            FhirSpecification.R4 => new R4StructureDefinitionSummaryProvider(),
            FhirSpecification.R4B => new R4BStructureDefinitionSummaryProvider(),
            FhirSpecification.R5 => new R5StructureDefinitionSummaryProvider(),
            FhirSpecification.R6 => new R6StructureDefinitionSummaryProvider(),
            FhirSpecification.Stu3 => new Stu3StructureDefinitionSummaryProvider(),
            _ => throw new NotSupportedException($"FHIR specification {spec} is not supported")
        };
    }
}
