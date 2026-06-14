// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;

namespace Ignixa.Serialization.SourceNodes;

/// <summary>
/// Base class for FHIR DomainResource facades, mirroring FHIR's <c>DomainResource</c> in the type
/// hierarchy (<c>Resource</c> -&gt; <c>DomainResource</c> -&gt; concrete resource). Generated resource
/// facades for DomainResources derive from this rather than directly from <see cref="ResourceJsonNode"/>.
/// </summary>
/// <remarks>
/// This type adds no members of its own in the current cut; it exists so the generated base/version
/// facades have a stable DomainResource-shaped base and so future shared DomainResource members
/// (text, contained, extension, modifierExtension) can be lifted here without touching consumers.
/// The constructor contract is identical to <see cref="ResourceJsonNode"/> and is chainable by
/// subclasses in other assemblies.
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
}
