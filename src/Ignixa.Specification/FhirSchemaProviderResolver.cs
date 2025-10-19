// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain;
using Ignixa.SourceNodeSerialization;

namespace Ignixa.Specification;

/// <summary>
/// Delegate for resolving FHIR schema providers based on FHIR version.
/// Enables version-aware components to get the correct provider at runtime.
/// </summary>
/// <param name="version">The FHIR version specification.</param>
/// <returns>The schema provider for the specified version.</returns>
public delegate IFhirSchemaProvider FhirSchemaProviderResolver(FhirSpecification version);
