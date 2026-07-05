// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Carries the per-run dependencies an <see cref="IResourceGraphAugmentor"/> needs: the schema
/// provider, a faker for any new resources it creates, and the clock backing deterministic timestamps.
/// </summary>
public sealed class ResourceGraphAugmentationContext
{
    /// <summary>The FHIR schema provider for the target FHIR version.</summary>
    public required IFhirSchemaProvider SchemaProvider { get; init; }

    /// <summary>The faker shared across this generation run.</summary>
    public required SchemaBasedFhirResourceFaker Faker { get; init; }

    /// <summary>The clock backing generated timestamps.</summary>
    public required TimeProvider Clock { get; init; }
}
