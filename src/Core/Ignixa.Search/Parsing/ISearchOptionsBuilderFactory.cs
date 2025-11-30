// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization;

namespace Ignixa.Search.Parsing;

/// <summary>
/// Factory for creating version-aware SearchOptionsBuilder instances.
/// </summary>
public interface ISearchOptionsBuilderFactory
{
    /// <summary>
    /// Creates a SearchOptionsBuilder for the specified FHIR version.
    /// </summary>
    /// <param name="fhirVersion">The FHIR version specification.</param>
    /// <returns>A SearchOptionsBuilder configured for the specified version.</returns>
    ISearchOptionsBuilder Create(FhirSpecification fhirVersion);
}
