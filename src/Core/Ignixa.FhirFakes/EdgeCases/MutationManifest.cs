// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// The complete, replayable record of every mutation the pipeline applied to one resource.
/// Carries the seed so a single resource can be reproduced in isolation.
/// </summary>
public sealed class MutationManifest
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>Initializes a new manifest.</summary>
    /// <param name="resourceId">The id of the mutated resource.</param>
    /// <param name="seed">The pipeline seed used to produce these mutations.</param>
    /// <param name="mutations">The ordered list of applied mutations.</param>
    public MutationManifest(string resourceId, int seed, IReadOnlyList<MutationRecord> mutations)
    {
        ArgumentNullException.ThrowIfNull(resourceId);
        ArgumentNullException.ThrowIfNull(mutations);
        ResourceId = resourceId;
        Seed = seed;
        Mutations = mutations;
    }

    /// <summary>The id of the mutated resource.</summary>
    public string ResourceId { get; }

    /// <summary>The pipeline seed used to produce these mutations.</summary>
    public int Seed { get; }

    /// <summary>The ordered list of applied mutations.</summary>
    public IReadOnlyList<MutationRecord> Mutations { get; }

    /// <summary>Serializes this manifest to indented JSON.</summary>
    public string ToJson()
    {
        var dto = new
        {
            resourceId = ResourceId,
            seed = Seed,
            mutations = Mutations.Select(m => new
            {
                category = m.Category,
                path = m.Path,
                before = m.Before,
                after = m.After,
            }),
        };

        return JsonSerializer.Serialize(dto, SerializerOptions);
    }
}
