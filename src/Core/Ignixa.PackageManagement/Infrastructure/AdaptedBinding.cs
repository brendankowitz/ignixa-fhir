// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Concrete <see cref="IBinding"/> emitted by the adapter.
/// </summary>
internal sealed class AdaptedBinding : IBinding
{
    public AdaptedBinding(string strength, string? valueSet, string? description)
    {
        Strength = strength ?? throw new ArgumentNullException(nameof(strength));
        ValueSet = valueSet;
        Description = description;
    }

    public string Strength { get; }
    public string? ValueSet { get; }
    public string? Description { get; }
}
