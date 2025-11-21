// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Specification;

namespace Ignixa.Extensions.ProfileBehaviors.Abstractions;

/// <summary>
/// Metadata about a FHIR element from the schema.
/// Lightweight wrapper around IElementDefinitionSummary.
/// </summary>
public sealed class ElementMetadata
{
    /// <summary>
    /// Element name (e.g., "name", "birthDate").
    /// </summary>
    public required string ElementName { get; init; }

    /// <summary>
    /// Is this a collection (array)?
    /// </summary>
    public bool IsCollection { get; init; }

    /// <summary>
    /// Is this element required (min > 0)?
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// Is this a choice type (e.g., value[x])?
    /// </summary>
    public bool IsChoiceElement { get; init; }

    /// <summary>
    /// Element types (e.g., "string", "Reference(Patient)").
    /// </summary>
    public IReadOnlyList<string> Types { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Binding strength (for coded elements): required, extensible, preferred, example.
    /// </summary>
    public string? BindingStrength { get; init; }

    /// <summary>
    /// Creates ElementMetadata from IElementDefinitionSummary.
    /// Extracts binding strength from IExtendedElementMetadata if available.
    /// </summary>
    public static ElementMetadata FromElementDefinition(IElementDefinitionSummary element)
    {
        // Extract binding strength if element has extended metadata
        string? bindingStrength = null;
        if (element is IExtendedElementMetadata extended && extended.Binding != null)
        {
            bindingStrength = extended.Binding.Strength;
        }

        return new ElementMetadata
        {
            ElementName = element.ElementName,
            IsCollection = element.IsCollection,
            IsRequired = element.IsRequired,
            IsChoiceElement = element.IsChoiceElement,
            Types = element.Type?.Select(t => t.GetTypeName()).ToList() ?? Array.Empty<string>(),
            BindingStrength = bindingStrength
        };
    }
}
