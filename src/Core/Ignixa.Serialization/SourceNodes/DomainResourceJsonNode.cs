// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Models;

namespace Ignixa.Serialization.SourceNodes;

/// <summary>
/// Base class for FHIR DomainResource facades, mirroring FHIR's <c>DomainResource</c> in the type
/// hierarchy (<c>Resource</c> -&gt; <c>DomainResource</c> -&gt; concrete resource). Generated resource
/// facades for DomainResources derive from this rather than directly from <see cref="ResourceJsonNode"/>.
/// </summary>
/// <remarks>
/// Provides the shared DomainResource-level properties: <c>text</c>, <c>contained</c>,
/// <c>extension</c>, and <c>modifierExtension</c>. These are defined once here so the generator
/// does not duplicate them on every concrete DomainResource facade.
/// </remarks>
public class DomainResourceJsonNode : ResourceJsonNode
{
    /// <summary>
    /// Default constructor for deserialization.
    /// </summary>
    public DomainResourceJsonNode()
    {
    }

    /// <summary>
    /// Protected internal constructor for derived types (accepts a pre-parsed JsonObject).
    /// </summary>
    /// <param name="jsonObject">Existing JsonObject to wrap.</param>
    protected internal DomainResourceJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    /// <summary>
    /// Protected internal constructor for derived types (accepts a pre-parsed JsonObject and optional FHIR version).
    /// </summary>
    /// <param name="jsonObject">Existing JsonObject to wrap.</param>
    /// <param name="fhirVersion">Optional FHIR version (inherited from parent). Can be null.</param>
    protected internal DomainResourceJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public MutableJsonList<ResourceJsonNode> Contained => GetListProperty<ResourceJsonNode>("contained");

    [JsonIgnore]
    public Narrative? Text
    {
        get => GetComplexProperty<Narrative>("text");
        set => SetProperty("text", value?.MutableNode);
    }

    [JsonIgnore]
    public MutableJsonList<Extension> Extension => GetListProperty<Extension>("extension");

    [JsonIgnore]
    public MutableJsonList<Extension> ModifierExtension => GetListProperty<Extension>("modifierExtension");
}
