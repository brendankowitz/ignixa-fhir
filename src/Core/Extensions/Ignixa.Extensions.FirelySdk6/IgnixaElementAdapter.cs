// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Specification;
using Ignixa.Abstractions;
// Firely SDK types
using FirelyElementDef = Hl7.Fhir.Specification.IElementDefinitionSummary;

namespace Ignixa.Extensions.FirelySdk;

/// <summary>
/// Adapts Firely SDK's ITypedElement to Ignixa's IElement.
/// Provides Firely → Ignixa conversion with lazy materialization.
/// </summary>
/// <remarks>
/// This adapter enables using Firely SDK types with Ignixa-based libraries.
/// Children are lazily materialized to avoid allocation storms.
/// </remarks>
public class IgnixaElementAdapter : IElement
{
    private readonly Hl7.Fhir.ElementModel.ITypedElement _firelyElement;
    private IReadOnlyList<IElement>? _cachedAllChildren;
    private Dictionary<string, IReadOnlyList<IElement>>? _childrenByName;

    /// <summary>
    /// Creates a new adapter wrapping a Firely SDK ITypedElement.
    /// </summary>
    /// <param name="firelyElement">Firely SDK typed element to adapt</param>
    public IgnixaElementAdapter(Hl7.Fhir.ElementModel.ITypedElement firelyElement)
    {
        _firelyElement = firelyElement ?? throw new ArgumentNullException(nameof(firelyElement));
    }

    /// <inheritdoc/>
    public string Name => _firelyElement.Name;

    /// <inheritdoc/>
    public object? Value => _firelyElement.Value;

    /// <inheritdoc/>
    public string InstanceType => _firelyElement.InstanceType ?? string.Empty;

    /// <inheritdoc/>
    public string Location => _firelyElement.Location;

    /// <inheritdoc/>
    public bool HasPrimitiveValue => _firelyElement.Value != null;

    /// <inheritdoc/>
    public IType? Type => _firelyElement.Definition != null
        ? new TypeAdapter(_firelyElement.Definition)
        : null;

    /// <inheritdoc/>
    public IReadOnlyList<IElement> Children(string? name = null)
    {
        // Materialize all children on first access (lazy)
        if (_cachedAllChildren == null)
        {
            _cachedAllChildren = _firelyElement.Children()
                .Select(child => (IElement)new IgnixaElementAdapter(child))
                .ToArray();
        }

        // Filter by name if specified
        if (name == null)
            return _cachedAllChildren;

        // Cache filtered children to avoid repeated allocations
        _childrenByName ??= new Dictionary<string, IReadOnlyList<IElement>>();

        if (!_childrenByName.TryGetValue(name, out var filtered))
        {
            filtered = _cachedAllChildren.Where(c => c.Name == name).ToArray();
            _childrenByName[name] = filtered;
        }

        return filtered;
    }

    /// <inheritdoc/>
    public T? Meta<T>() where T : class
    {
        // Store the original Firely element as metadata for unwrapping
        if (typeof(T) == typeof(Hl7.Fhir.ElementModel.ITypedElement))
            return _firelyElement as T;

        // ITypedElement doesn't have a general metadata mechanism
        return null;
    }

    /// <summary>
    /// Adapter for Firely's IElementDefinitionSummary → Ignixa's IType.
    /// </summary>
    private class TypeAdapter : IType
    {
        private readonly FirelyElementDef _definition;
        private TypeInfo? _cachedInfo;

        public TypeAdapter(FirelyElementDef definition)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public TypeInfo Info
        {
            get
            {
                if (_cachedInfo.HasValue)
                    return _cachedInfo.Value;

                var typeName = _definition.Type.FirstOrDefault()?.GetTypeName() ?? _definition.ElementName;
                var primitive = FhirPrimitiveExtensions.FromTypeString(typeName);

                _cachedInfo = new TypeInfo(
                    name: typeName,
                    primitive: primitive,
                    isResource: _definition.IsResource,
                    isAbstract: false,  // Not available in IElementDefinitionSummary
                    isChoiceElement: _definition.IsChoiceElement,
                    isModifier: _definition.IsModifier
                );

                return _cachedInfo.Value;
            }
        }

        public bool IsCollection => _definition.IsCollection;
        public bool IsRequired => _definition.IsRequired;
        public bool InSummary => _definition.InSummary;
        public int Order => _definition.Order;

        public IReadOnlyList<IType> Children => Array.Empty<IType>();  // Not supported in adapter
    }
}
