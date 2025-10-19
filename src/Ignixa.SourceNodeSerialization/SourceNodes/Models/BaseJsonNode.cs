// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Ignixa.SourceNodeSerialization.SourceNodes.Models;

/// <summary>
/// Base class for all *JsonNode model classes that wrap a mutable JsonObject.
/// Provides common GetMutableNode() and SetProperty() implementations.
/// </summary>
public abstract class BaseJsonNode : IMutableJsonNode
{
    /// <summary>
    /// Internal storage: Single source of truth (no caching, direct read/write).
    /// </summary>
    private readonly JsonObject _internalNode;

    /// <summary>
    /// Gets or sets the FHIR version for this node.
    /// Set at root node construction and propagated to children.
    /// Used to determine version-specific serialization behavior.
    /// </summary>
    public FhirSpecification? FhirVersion { get; set; }

    /// <summary>
    /// Default constructor for deserialization.
    /// </summary>
    protected BaseJsonNode()
    {
        _internalNode = new JsonObject();
    }

    /// <summary>
    /// Constructor with FHIR version awareness.
    /// </summary>
    /// <param name="fhirVersion">The FHIR version (Stu3, R4, R4B, R5).</param>
    protected BaseJsonNode(FhirSpecification fhirVersion)
        : this()
    {
        FhirVersion = fhirVersion;
    }

    /// <summary>
    /// Internal constructor for wrapping existing JsonObject (used when accessing nested properties).
    /// </summary>
    /// <param name="jsonObject">Existing JsonObject to wrap.</param>
    /// <param name="fhirVersion">Optional FHIR version (inherited from parent).</param>
    protected BaseJsonNode(JsonObject jsonObject, FhirSpecification? fhirVersion = null)
    {
        _internalNode = jsonObject ?? throw new ArgumentNullException(nameof(jsonObject));
        FhirVersion = fhirVersion;
    }

    /// <summary>
    /// Gets the internal mutable JsonObject for direct manipulation.
    /// Use this for FHIR Patch operations or reference updates.
    /// All changes are immediately reflected in the resource.
    /// </summary>
    public JsonObject MutableNode => _internalNode;

    /// <summary>
    /// Sets a property value in the node.
    /// Convenience method for common mutations.
    /// </summary>
    /// <param name="name">The property name (e.g., "active", "name", "telecom").</param>
    /// <param name="value">The JsonNode value to set.</param>
    public void SetProperty(string name, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (value == null)
        {
            _internalNode.Remove(name);
        }
        else
        {
            _internalNode[name] = value;
        }
    }
}
