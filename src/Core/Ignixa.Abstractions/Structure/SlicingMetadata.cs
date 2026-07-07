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
        string rules,
        bool ordered,
        IReadOnlyList<SliceDefinition>? slices = null)
    {
        Discriminators = discriminators ?? Array.Empty<DiscriminatorDefinition>();
        Rules = rules;
        Ordered = ordered;
        Slices = slices ?? Array.Empty<SliceDefinition>();
    }

    /// <summary>
    /// Compatibility bridge for the legacy <c>Type:Path</c> string form emitted by pre-M2 code-gen
    /// (e.g. <c>"Value:url"</c>). Parses each entry into a structured <see cref="DiscriminatorDefinition"/>
    /// so already-generated provider files continue to compile unchanged. New code-gen emits the
    /// structured constructor above.
    /// </summary>
    /// <param name="discriminators">Legacy discriminator strings in <c>Type:Path</c> form.</param>
    /// <param name="rules">Slicing rules.</param>
    /// <param name="ordered">Whether slices must appear in order.</param>
    public SlicingMetadata(string[] discriminators, string rules, bool ordered)
        : this(ParseLegacy(discriminators), rules, ordered, null)
    {
    }

    /// <summary>Gets the structured discriminators (type + path).</summary>
    public IReadOnlyList<DiscriminatorDefinition> Discriminators { get; }

    /// <summary>Gets the slicing rules: <c>Open</c>, <c>Closed</c>, or <c>OpenAtEnd</c>.</summary>
    public string Rules { get; }

    /// <summary>Gets a value indicating whether slices must appear in order.</summary>
    public bool Ordered { get; }

    /// <summary>Gets the named slices carried by a profile; empty for a bare slicing header.</summary>
    public IReadOnlyList<SliceDefinition> Slices { get; }

    private static IReadOnlyList<DiscriminatorDefinition> ParseLegacy(string[] discriminators)
    {
        if (discriminators is null || discriminators.Length == 0)
        {
            return Array.Empty<DiscriminatorDefinition>();
        }

        var result = new List<DiscriminatorDefinition>(discriminators.Length);
        foreach (var raw in discriminators)
        {
            if (string.IsNullOrEmpty(raw))
            {
                continue;
            }

            var separator = raw.IndexOf(':', StringComparison.Ordinal);
            var typeToken = separator >= 0 ? raw[..separator] : "value";
            var path = separator >= 0 ? raw[(separator + 1)..] : raw;
            result.Add(new DiscriminatorDefinition(ParseType(typeToken), path));
        }

        return result;
    }

    private static DiscriminatorType ParseType(string token) => token.ToUpperInvariant() switch
    {
        "PATTERN" => DiscriminatorType.Pattern,
        "EXISTS" => DiscriminatorType.Exists,
        "TYPE" => DiscriminatorType.Type,
        "PROFILE" => DiscriminatorType.Profile,
        _ => DiscriminatorType.Value,
    };
}
