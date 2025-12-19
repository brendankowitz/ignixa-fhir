// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Experimental.Ips.Api;

/// <summary>
/// Factory for creating IPS generation strategies from StructureDefinition resources.
/// </summary>
public interface IStructureDefinitionStrategyFactory
{
    /// <summary>
    /// Creates a strategy from a Composition profile StructureDefinition.
    /// Returns null if the StructureDefinition is not a patient summary profile.
    /// </summary>
    IIpsGenerationStrategy? CreateFromStructureDefinition(
        ResourceJsonNode compositionProfile,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Identifies if a StructureDefinition represents a patient summary Composition.
    /// Heuristics:
    /// - resourceType == "Composition"
    /// - baseDefinition points to IPS or derivative
    /// - Has section slicing with LOINC codes
    /// </summary>
    bool IsPatientSummaryProfile(ResourceJsonNode structureDefinition);
}
