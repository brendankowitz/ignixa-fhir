// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// A data record of the seed and every mutation the pipeline applied to one resource, for
/// reference and replay. This type is a DTO and does not itself perform replay: replay is achieved
/// by re-running <c>EdgeCasePipeline</c> with the same seed against the same input resource and
/// strategy set, per the pipeline's determinism contract.
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
                description = m.Description,
            }),
        };

        return JsonSerializer.Serialize(dto, SerializerOptions);
    }
}
