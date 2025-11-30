// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Converts package resource JSON to IType for use in composite schema provider.
/// Adapts conformance resources from FHIR NPM packages (IGs) to the schema provider interface.
/// </summary>
public interface IPackageResourceProvider
{
    /// <summary>
    /// Converts a package resource JSON to an IType.
    /// Used to treat IG profiles as first-class schema definitions.
    /// </summary>
    /// <param name="resourceJson">The FHIR StructureDefinition resource as JSON string.</param>
    /// <param name="fhirVersion">The FHIR version (e.g., "4.0.1", "4.3.0", "5.0.0").</param>
    /// <returns>The type definition if parsing succeeds, null otherwise.</returns>
    IType? ToTypeDefinition(string resourceJson, string fhirVersion);
}
