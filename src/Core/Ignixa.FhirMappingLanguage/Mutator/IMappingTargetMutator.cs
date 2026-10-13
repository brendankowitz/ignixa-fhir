// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.FhirMappingLanguage.Mutator;

/// <summary>
/// Schema-aware construction and mutation of FML targets, including nested target aliases.
/// </summary>
public interface IMappingTargetMutator : IJsonNodeMutator
{
    /// <summary>
    /// Creates a schema-aware element from a scalar, dictionary, JSON node, or element.
    /// A null value creates an empty instance. Only resources receive a resourceType property.
    /// </summary>
    IElement CreateElement(string typeName, object? value = null);

    /// <summary>
    /// Gets the target property's schema definition for implicit type-group dispatch.
    /// </summary>
    IType GetTargetDefinition(IElement parent, string propertyName);

    /// <summary>
    /// Resolves the target property's type without a source value.
    /// Throws when the schema and property name do not identify a single concrete type.
    /// </summary>
    string GetTargetType(IElement parent, string propertyName);

    /// <summary>
    /// Copies a value into a JSON-backed parent, or creates an empty child when value is null.
    /// Collections append unless Replace is specified; insertFirst inserts at index zero.
    /// Returns an element backed by the inserted target node, not the source node.
    /// Choice properties require an unambiguous type from the value or property name.
    /// </summary>
    IElement SetTarget(
        IElement parent,
        string propertyName,
        IElement? value,
        PropertyMutationMode mode = PropertyMutationMode.AutoDetect,
        bool insertFirst = false);

    /// <summary>
    /// Rebuilds the schema-aware view over the same JSON node to observe subsequent mutations.
    /// Previously obtained child lists remain snapshots.
    /// </summary>
    IElement Refresh(IElement element);

    /// <summary>
    /// Moves a live collection item and its primitive metadata to an index, or to the end when null.
    /// </summary>
    void MoveTarget(IElement element, int? index = null);
}
