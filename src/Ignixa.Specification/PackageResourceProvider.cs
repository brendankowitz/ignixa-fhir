// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Specification;

/// <summary>
/// Converts package resource JSON to IStructureDefinitionSummary for use in composite schema provider.
/// Parses FHIR StructureDefinition JSON using internal infrastructure (no Firely SDK).
/// </summary>
public class PackageResourceProvider : IPackageResourceProvider
{
    private readonly ILogger<PackageResourceProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackageResourceProvider"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public PackageResourceProvider(ILogger<PackageResourceProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Converts a package resource JSON to an IStructureDefinitionSummary.
    /// Parses FHIR StructureDefinition JSON and builds element summaries from snapshot.element array.
    /// </summary>
    /// <param name="resourceJson">The FHIR StructureDefinition resource as JSON string.</param>
    /// <param name="fhirVersion">The FHIR version (e.g., "4.0.1", "4.3.0", "5.0.0").</param>
    /// <returns>The structure definition summary if parsing succeeds, null otherwise.</returns>
    public IStructureDefinitionSummary? ToStructureDefinitionSummary(
        string resourceJson,
        string fhirVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(fhirVersion);

        // Use typed wrapper instead of raw JSON parsing
        var sdNode = StructureDefinitionJsonNode.Parse(resourceJson, _logger);
        if (sdNode == null)
        {
            return null;
        }

        // Validate required 'type' field
        if (string.IsNullOrWhiteSpace(sdNode.Type))
        {
            _logger.LogWarning("StructureDefinition missing 'type' field (url: {Url})", sdNode.Url);
            return null;
        }

        // Determine if this is a resource type
        var isResource = string.Equals(sdNode.Kind, "resource", StringComparison.OrdinalIgnoreCase);

        // Extract snapshot.element array
        var elements = sdNode.GetSnapshotElements();
        if (elements == null || elements.Count == 0)
        {
            _logger.LogWarning(
                "StructureDefinition has no snapshot.element array (url: {Url}, name: {Name}). Cannot build schema.",
                sdNode.Url,
                sdNode.Name);
            return null;
        }

        _logger.LogDebug(
            "Parsed StructureDefinition: type={Type}, kind={Kind}, isAbstract={IsAbstract}, elements={ElementCount}, version={FhirVersion}",
            sdNode.Type,
            sdNode.Kind,
            sdNode.IsAbstract,
            elements.Count,
            fhirVersion);

        // Build structure definition summary with lazy element parsing
        return new PackageStructureDefinitionSummary(
            typeName: sdNode.Type,
            isAbstract: sdNode.IsAbstract,
            isResource: isResource,
            url: sdNode.Url,
            elementsJson: elements,
            logger: _logger);
    }

    /// <summary>
    /// Internal implementation of IStructureDefinitionSummary for package resources.
    /// Lazy-loads element summaries on first call to GetElements().
    /// </summary>
    private sealed class PackageStructureDefinitionSummary : IStructureDefinitionSummary, IStructureDefinitionReference
    {
        private readonly JsonArray _elementsJson;
        private readonly ILogger _logger;
        private IReadOnlyCollection<IElementDefinitionSummary>? _elements;

        public PackageStructureDefinitionSummary(
            string typeName,
            bool isAbstract,
            bool isResource,
            string? url,
            JsonArray elementsJson,
            ILogger logger)
        {
            TypeName = typeName;
            IsAbstract = isAbstract;
            IsResource = isResource;
            Url = url;
            _elementsJson = elementsJson;
            _logger = logger;
        }

        public string TypeName { get; }
        public bool IsAbstract { get; }
        public bool IsResource { get; }
        public string? Url { get; }

        // IStructureDefinitionReference implementation (for choice type validation)
        public string ReferredType => TypeName;

        public IReadOnlyCollection<IElementDefinitionSummary> GetElements()
        {
            if (_elements != null)
            {
                return _elements;
            }

            var elementList = new List<IElementDefinitionSummary>(_elementsJson.Count);
            var order = 0;

            foreach (var elementJson in _elementsJson)
            {
                try
                {
                    var element = ParseElement(elementJson?.AsObject(), order++);
                    if (element != null)
                    {
                        elementList.Add(element);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse element in StructureDefinition {TypeName}", TypeName);
                }
            }

            _elements = elementList;
            return _elements;
        }

        private IElementDefinitionSummary? ParseElement(JsonObject? elementJson, int order)
        {
            if (elementJson == null)
            {
                return null;
            }

            // Extract required properties
            var path = elementJson["path"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            // Element name is the last part of the path
            var elementName = path.Contains('.', StringComparison.Ordinal) ? path[(path.LastIndexOf('.') + 1)..] : path;

            // Extract cardinality
            var minValue = elementJson["min"]?.GetValue<int>() ?? 0;
            var maxValue = elementJson["max"]?.GetValue<string>() ?? "1";
            var isCollection = maxValue == "*" || (int.TryParse(maxValue, out var max) && max > 1);
            var isRequired = minValue > 0;

            // Extract type information
            var typeArray = elementJson["type"] as JsonArray;
            var types = ParseTypes(typeArray);
            var isChoiceElement = types.Length > 1;

            // Extract flags
            var isSummary = elementJson["isSummary"]?.GetValue<bool>() ?? false;
            var isModifier = elementJson["isModifier"]?.GetValue<bool>() ?? false;

            // Determine if this is a resource type element
            var isResource = types.Any(t =>
            {
                var typeName = t.GetTypeName();
                return string.Equals(typeName, "Resource", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(typeName, "DomainResource", StringComparison.OrdinalIgnoreCase);
            });

            // Default type name for choice elements
            var defaultTypeName = types.Length > 0 ? types[0].GetTypeName() : string.Empty;

            // XML representation
            var representation = XmlRepresentation.XmlElement;
            var representationArray = elementJson["representation"] as JsonArray;
            if (representationArray != null && representationArray.Count > 0)
            {
                var repValue = representationArray[0]?.GetValue<string>();
                representation = repValue switch
                {
                    "xmlAttr" => XmlRepresentation.XmlAttr,
                    "xmlText" => XmlRepresentation.XmlText,
                    "typeAttr" => XmlRepresentation.TypeAttr,
                    "cdaText" => XmlRepresentation.CdaText,
                    "xhtml" => XmlRepresentation.XHtml,
                    _ => XmlRepresentation.XmlElement
                };
            }

            return new PackageElementDefinitionSummary(
                elementName: elementName,
                isCollection: isCollection,
                isRequired: isRequired,
                inSummary: isSummary,
                isChoiceElement: isChoiceElement,
                isResource: isResource,
                isModifier: isModifier,
                type: types,
                defaultTypeName: defaultTypeName,
                nonDefaultNamespace: null,
                representation: representation,
                order: order);
        }

        private ITypeSerializationInfo[] ParseTypes(JsonArray? typeArray)
        {
            if (typeArray == null || typeArray.Count == 0)
            {
                return Array.Empty<ITypeSerializationInfo>();
            }

            var types = new List<ITypeSerializationInfo>(typeArray.Count);

            foreach (var typeJson in typeArray)
            {
                var typeObj = typeJson?.AsObject();
                var code = typeObj?["code"]?.GetValue<string>();

                if (!string.IsNullOrWhiteSpace(code))
                {
                    types.Add(new TypeReference(code));
                }
            }

            return types.ToArray();
        }
    }

    /// <summary>
    /// Internal implementation of IElementDefinitionSummary for package resources.
    /// Minimal implementation covering core IElementDefinitionSummary properties.
    /// </summary>
    private sealed class PackageElementDefinitionSummary : IElementDefinitionSummary
    {
        public PackageElementDefinitionSummary(
            string elementName,
            bool isCollection,
            bool isRequired,
            bool inSummary,
            bool isChoiceElement,
            bool isResource,
            bool isModifier,
            ITypeSerializationInfo[] type,
            string defaultTypeName,
            string? nonDefaultNamespace,
            XmlRepresentation representation,
            int order)
        {
            ElementName = elementName;
            IsCollection = isCollection;
            IsRequired = isRequired;
            InSummary = inSummary;
            IsChoiceElement = isChoiceElement;
            IsResource = isResource;
            IsModifier = isModifier;
            Type = type;
            DefaultTypeName = defaultTypeName;
            NonDefaultNamespace = nonDefaultNamespace;
            Representation = representation;
            Order = order;
        }

        public string ElementName { get; }
        public bool IsCollection { get; }
        public bool IsRequired { get; }
        public bool InSummary { get; }
        public bool IsChoiceElement { get; }
        public bool IsResource { get; }
        public bool IsModifier { get; }
        public ITypeSerializationInfo[] Type { get; }
        public string? DefaultTypeName { get; }
        public string? NonDefaultNamespace { get; }
        public XmlRepresentation Representation { get; }
        public int Order { get; }
    }

    /// <summary>
    /// Simple type reference implementation.
    /// </summary>
    private sealed class TypeReference : IStructureDefinitionReference
    {
        public TypeReference(string referredType)
        {
            ReferredType = referredType;
        }

        public string ReferredType { get; }
    }
}
