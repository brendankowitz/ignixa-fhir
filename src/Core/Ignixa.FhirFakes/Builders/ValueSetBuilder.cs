// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using System.Text.Json.Nodes;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Builders;

/// <summary>
/// Fluent builder for generating ValueSet resources.
/// Provides clean API for test data setup without manual JSON manipulation.
/// </summary>
/// <remarks>
/// Example Usage:
/// <code>
/// var valueSet = ValueSetBuilder.Create(schemaProvider)
///     .WithUrl("http://example.org/fhir/ValueSet/test")
///     .WithName("TestValueSet")
///     .WithStatus("active")
///     .WithTag(tag)
///     .Build();
/// </code>
/// </remarks>
public sealed class ValueSetBuilder : FhirResourceBuilder<ValueSetBuilder>
{
    private string? _url;
    private string? _name;
    private string? _title;
    private string? _status = "active";
    private string? _version;
    private string? _description;
    private bool? _experimental;
    private string? _publisher;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValueSetBuilder"/> class.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    public ValueSetBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Creates a new ValueSetBuilder instance.
    /// </summary>
    public static ValueSetBuilder Create(IFhirSchemaProvider schemaProvider)
    {
        return new ValueSetBuilder(schemaProvider);
    }

    /// <summary>
    /// Sets the canonical URL for the value set.
    /// This is the primary field for URI search testing.
    /// </summary>
    public ValueSetBuilder WithUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        _url = url;
        return this;
    }

    /// <summary>
    /// Sets the name for the value set.
    /// </summary>
    public ValueSetBuilder WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        return this;
    }

    /// <summary>
    /// Sets the title for the value set.
    /// </summary>
    public ValueSetBuilder WithTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        _title = title;
        return this;
    }

    /// <summary>
    /// Sets the status (draft | active | retired | unknown).
    /// Defaults to "active".
    /// </summary>
    public ValueSetBuilder WithStatus(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _status = status;
        return this;
    }

    /// <summary>
    /// Sets the version for the value set.
    /// </summary>
    public ValueSetBuilder WithVersion(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        _version = version;
        return this;
    }

    /// <summary>
    /// Sets the description for the value set.
    /// </summary>
    public ValueSetBuilder WithDescription(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        _description = description;
        return this;
    }

    /// <summary>
    /// Sets whether the value set is experimental.
    /// </summary>
    public ValueSetBuilder WithExperimental(bool experimental)
    {
        _experimental = experimental;
        return this;
    }

    /// <summary>
    /// Sets the publisher of the value set.
    /// </summary>
    public ValueSetBuilder WithPublisher(string publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        _publisher = publisher;
        return this;
    }

    /// <summary>
    /// Builds the ValueSet resource with all configured properties.
    /// </summary>
    public override ResourceJsonNode Build()
    {
        var valueSetJson = new JsonObject
        {
            ["resourceType"] = "ValueSet",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta()
        };

        if (!string.IsNullOrEmpty(_url))
        {
            valueSetJson["url"] = _url;
        }

        if (!string.IsNullOrEmpty(_name))
        {
            valueSetJson["name"] = _name;
        }

        if (!string.IsNullOrEmpty(_title))
        {
            valueSetJson["title"] = _title;
        }

        if (!string.IsNullOrEmpty(_status))
        {
            valueSetJson["status"] = _status;
        }

        if (!string.IsNullOrEmpty(_version))
        {
            valueSetJson["version"] = _version;
        }

        if (!string.IsNullOrEmpty(_description))
        {
            valueSetJson["description"] = _description;
        }

        if (_experimental.HasValue)
        {
            valueSetJson["experimental"] = _experimental.Value;
        }

        if (!string.IsNullOrEmpty(_publisher))
        {
            valueSetJson["publisher"] = _publisher;
        }

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(valueSetJson);
    }
}
