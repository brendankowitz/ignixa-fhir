// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Specification;
using Ignixa.Abstractions;
// Firely SDK types - use these for implementing Firely interfaces
using FirelyElementDef = Hl7.Fhir.Specification.IElementDefinitionSummary;
using FirelyTypeInfo = Hl7.Fhir.Specification.ITypeSerializationInfo;
using FirelyXmlRep = Hl7.Fhir.Specification.XmlRepresentation;
using FirelyStructureRef = Hl7.Fhir.Specification.IStructureDefinitionReference;

namespace Ignixa.Extensions.FirelySdk;

/// <summary>
/// Adapts Ignixa's IElement to Firely SDK's ITypedElement.
/// Provides Ignixa → Firely conversion for interop scenarios.
/// </summary>
/// <remarks>
/// This adapter enables using Ignixa types with Firely SDK-based tools
/// (e.g., Hl7.FhirPath, Firely Validator).
/// </remarks>
public class TypedElementAdapter : ITypedElement
{
    private readonly IElement _coreElement;
    private object? _translatedValue;
    private volatile bool _translatedValueResolved;

    /// <summary>
    /// Creates a new adapter wrapping an Ignixa IElement.
    /// </summary>
    /// <param name="coreElement">Ignixa element to adapt</param>
    public TypedElementAdapter(IElement coreElement)
    {
        _coreElement = coreElement ?? throw new ArgumentNullException(nameof(coreElement));
    }

    /// <inheritdoc/>
    public string Name
    {
        get
        {
            // For choice elements, ToPoco() expects the base name (e.g., "effective")
            // not the suffixed name (e.g., "effectiveDateTime") for POCO property mapping.
            // The element name in data is "effectiveDateTime", but the POCO property is "Effective".
            if (_coreElement.Type is { Info.IsChoiceElement: true } type)
            {
                // Return the base name from schema (e.g., "effective" not "effectiveDateTime")
                return type.Info.Name;
            }

            // For non-choice elements, return the actual element name
            return _coreElement.Name;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <inheritdoc path="/remarks/node()"/>
    /// <para>
    /// Translated into Firely's representation - Ignixa carries the temporal primitives as FHIR
    /// wire-format strings, where Firely expects <c>Hl7.Fhir.ElementModel.Types</c> instances.
    /// </para>
    /// <para>
    /// Memoized because the translation parses, and Firely's engines read <c>Value</c> repeatedly
    /// on the same element - and because <see cref="Children"/> hands out a fresh adapter per call,
    /// so nothing upstream would amortise it. The value and the instance type are both captured on
    /// first read: this adapter is a snapshot of the wrapped element, not a live view of it, so
    /// re-create it after mutating the element underneath. The resolved flag is
    /// <c>volatile</c> so a concurrent reader cannot see it set before the value it guards; the
    /// translation is pure, so racing readers at worst repeat the work.
    /// </para>
    /// </remarks>
    public object? Value
    {
        get
        {
            if (!_translatedValueResolved)
            {
                _translatedValue = FirelyPrimitiveValues.ToFirely(_coreElement.Value, _coreElement.InstanceType);
                _translatedValueResolved = true;
            }

            return _translatedValue;
        }
    }

    /// <inheritdoc/>
    public string InstanceType => _coreElement.InstanceType;

    /// <inheritdoc/>
    public string Location => _coreElement.Location;

    /// <inheritdoc/>
    public FirelyElementDef? Definition => _coreElement.Type != null
        ? new ElementDefinitionAdapter(_coreElement.Type)
        : null;

    /// <inheritdoc/>
    public IEnumerable<Hl7.Fhir.ElementModel.ITypedElement> Children(string? name = null)
    {
        // Convert IReadOnlyList to IEnumerable
        var children = _coreElement.Children(name);
        return children.Select(child => (Hl7.Fhir.ElementModel.ITypedElement)new TypedElementAdapter(child));
    }

    /// <inheritdoc/>
    public T? Annotation<T>() where T : class
    {
        // Store the original Ignixa element as metadata for unwrapping
        if (typeof(T) == typeof(IElement))
            return _coreElement as T;

        return _coreElement.Meta<T>();
    }

    /// <summary>
    /// Adapter for Ignixa's IType → Firely's IElementDefinitionSummary.
    /// </summary>
    private class ElementDefinitionAdapter : FirelyElementDef
    {
        private readonly IType _type;

        public ElementDefinitionAdapter(IType type)
        {
            _type = type ?? throw new ArgumentNullException(nameof(type));
        }

        public string ElementName => _type.Info.Name;
        public bool IsCollection => _type.IsCollection;
        public bool IsRequired => _type.IsRequired;
        public bool InSummary => _type.InSummary;
        public bool IsChoiceElement => _type.Info.IsChoiceElement;
        public bool IsResource => _type.Info.IsResource;
        public bool IsModifier => _type.Info.IsModifier;

        public FirelyTypeInfo[] Type
        {
            get
            {
                // Create a StructureDefinitionReference for the type
                return new FirelyTypeInfo[] { new StructureDefinitionRef(_type.Info.Name) };
            }
        }

        public string? DefaultTypeName => _type.Info.Name;
        public string? NonDefaultNamespace => null;  // Not supported
        public FirelyXmlRep Representation => FirelyXmlRep.XmlElement;  // Default representation for Firely SDK compatibility
        public int Order => _type.Order;
    }

    /// <summary>
    /// Simple IStructureDefinitionReference implementation.
    /// </summary>
    private class StructureDefinitionRef : FirelyStructureRef, FirelyTypeInfo
    {
        public StructureDefinitionRef(string referredType)
        {
            ReferredType = referredType ?? throw new ArgumentNullException(nameof(referredType));
        }

        public string ReferredType { get; }
    }
}
