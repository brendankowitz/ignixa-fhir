// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

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
