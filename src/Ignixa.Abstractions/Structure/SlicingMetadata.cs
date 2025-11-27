// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Slicing metadata from ElementDefinition.slicing.
/// NOTE: Slicing support is not yet implemented but metadata is captured for future use.
/// </summary>
public sealed class SlicingMetadata
{
    public SlicingMetadata(string[] discriminators, string rules, bool ordered)
    {
        Discriminators = discriminators;
        Rules = rules;
        Ordered = ordered;
    }

#pragma warning disable CA1819 // Properties should not return arrays - Codegen metadata requires arrays for slicing discriminators
    public string[] Discriminators { get; }
#pragma warning restore CA1819
    public string Rules { get; }
    public bool Ordered { get; }
}
