// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Slicing metadata from <c>ElementDefinition.slicing</c>: the structured discriminators, the
/// <c>rules</c> (open / closed / openAtEnd), the <c>ordered</c> flag, and the named
/// <see cref="Slices"/> constrained by the profile. Consumed by the validator's slicing check.
/// </summary>
public sealed class SlicingMetadata
{
    /// <summary>
    /// Initializes a new instance from structured discriminators and (optionally) named slices.
    /// This is the form emitted by current code-gen and by the differential→snapshot adapter.
    /// </summary>
    /// <param name="discriminators">Structured discriminators (type + path).</param>
    /// <param name="rules">Slicing rules: <c>Open</c>, <c>Closed</c>, or <c>OpenAtEnd</c>.</param>
    /// <param name="ordered">Whether slices must appear in order.</param>
    /// <param name="slices">Named slices carried by a profile; empty for a bare slicing header.</param>
    public SlicingMetadata(
        IReadOnlyList<DiscriminatorDefinition> discriminators,
        SlicingRules rules,
        bool ordered,
        IReadOnlyList<SliceDefinition>? slices = null)
    {
        Discriminators = discriminators ?? Array.Empty<DiscriminatorDefinition>();
        Rules = rules;
        Ordered = ordered;
        Slices = slices ?? Array.Empty<SliceDefinition>();
    }

    /// <summary>Gets the structured discriminators (type + path).</summary>
    public IReadOnlyList<DiscriminatorDefinition> Discriminators { get; }

    /// <summary>Gets the slicing rules: <c>Open</c>, <c>Closed</c>, or <c>OpenAtEnd</c>.</summary>
    public SlicingRules Rules { get; }

    /// <summary>Gets a value indicating whether slices must appear in order.</summary>
    public bool Ordered { get; }

    /// <summary>Gets the named slices carried by a profile; empty for a bare slicing header.</summary>
    public IReadOnlyList<SliceDefinition> Slices { get; }
}
