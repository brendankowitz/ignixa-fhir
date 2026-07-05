// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Adds workflow resources (appointments, lists, document references, topology) to an existing
/// resource graph. Implementations should be stateless with respect to execution: all per-run state
/// lives on <see cref="ResourceGraph"/> or <see cref="ResourceGraphAugmentationContext"/> rather than
/// mutable instance fields, so a single configured instance is safe to reuse.
/// </summary>
public interface IResourceGraphAugmentor
{
    /// <summary>Mutates <paramref name="graph"/> in place, adding this augmentor's resources.</summary>
    void Augment(ResourceGraph graph, ResourceGraphAugmentationContext context);
}
