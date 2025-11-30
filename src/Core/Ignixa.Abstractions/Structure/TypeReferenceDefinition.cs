// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

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
