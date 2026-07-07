// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// A named slice within a sliced element: its per-slice cardinality and the compiled discriminator
/// expectations used to assign candidate elements to it.
/// </summary>
public sealed class SliceDefinition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SliceDefinition"/> class.
    /// </summary>
    /// <param name="name">The slice name (<c>ElementDefinition.sliceName</c>).</param>
    /// <param name="min">Minimum per-slice cardinality.</param>
    /// <param name="max">Maximum per-slice cardinality; <c>null</c> for unbounded (<c>*</c>).</param>
    /// <param name="match">The compiled discriminator expectations a candidate must satisfy to be assigned here.</param>
    public SliceDefinition(string name, int min, int? max, IReadOnlyList<SliceDiscriminatorValue> match)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Min = min;
        Max = max;
        Match = match ?? Array.Empty<SliceDiscriminatorValue>();
    }

    /// <summary>Gets the slice name.</summary>
    public string Name { get; }

    /// <summary>Gets the minimum per-slice cardinality.</summary>
    public int Min { get; }

    /// <summary>Gets the maximum per-slice cardinality, or <c>null</c> for unbounded.</summary>
    public int? Max { get; }

    /// <summary>Gets the compiled discriminator expectations for assignment.</summary>
    public IReadOnlyList<SliceDiscriminatorValue> Match { get; }
}
