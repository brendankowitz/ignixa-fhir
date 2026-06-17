// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// A single mutable string-valued leaf in a resource tree. Concrete subtypes carry the parent
/// container reference required to mutate the leaf in place: <see cref="PropertyTarget"/> for an
/// object property and <see cref="ArrayItemTarget"/> for an array element.
/// </summary>
/// <remarks>
/// Modelled as a closed hierarchy so each subtype holds exactly the parent reference it needs.
/// This makes the "object property OR array element" choice a type-level distinction rather than a
/// runtime union of nullable fields, eliminating sentinel values and unreachable error states.
/// </remarks>
public abstract class MutationTarget
{
    /// <summary>Initializes the shared leaf metadata.</summary>
    /// <param name="elementName">The leaf element name (the owning property key).</param>
    /// <param name="path">The computed JSON path to this leaf.</param>
    /// <param name="value">The current string value of this leaf.</param>
    protected MutationTarget(string elementName, string path, string value)
    {
        ArgumentNullException.ThrowIfNull(elementName);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(value);
        ElementName = elementName;
        Path = path;
        Value = value;
    }

    /// <summary>The leaf element name (the property key, e.g. "family"). For array items this is the owning property name.</summary>
    public string ElementName { get; }

    /// <summary>The computed JSON path to this leaf (e.g. "name[0].family", "birthDate").</summary>
    public string Path { get; }

    /// <summary>The current string value of this leaf.</summary>
    public string Value { get; }

    /// <summary>Replaces this leaf's value in its parent container in place.</summary>
    public abstract void Replace(string newValue);
}
