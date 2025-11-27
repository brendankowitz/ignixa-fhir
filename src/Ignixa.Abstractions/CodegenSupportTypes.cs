// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Concrete implementation of IConstraint for codegen use.
/// </summary>
public sealed class ConstraintDefinition : IConstraint
{
    public required string Key { get; init; }
    public required string Expression { get; init; }
    public string? Human { get; init; }
    public required string Severity { get; init; }
    public string? Xpath { get; init; }
}

/// <summary>
/// Concrete implementation of IBinding for codegen use.
/// </summary>
public sealed class BindingMetadata : IBinding
{
    public BindingMetadata(string? valueSet, string strength, string? description = null)
    {
        ValueSet = valueSet;
        Strength = strength;
        Description = description;
    }

    public string Strength { get; }
    public string? ValueSet { get; }
    public string? Description { get; }
}

/// <summary>
/// Concrete implementation of ITypeReference for codegen use.
/// </summary>
public sealed class TypeReferenceDefinition : ITypeReference
{
    public TypeReferenceDefinition(
        string code,
        string? profile = null,
        string? targetProfile = null,
        IReadOnlyList<string>? aggregation = null,
        string? versioning = null)
    {
        Code = code;
        Profile = profile;
        TargetProfile = targetProfile;
        Aggregation = aggregation;
        Versioning = versioning;
    }

    public string Code { get; }
    public string? Profile { get; }
    public string? TargetProfile { get; }
    public IReadOnlyList<string>? Aggregation { get; }
    public string? Versioning { get; }
}

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
