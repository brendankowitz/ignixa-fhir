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
    private object? _translatedValue;
    private volatile bool _translatedValueResolved;

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
    /// <remarks>
    /// <inheritdoc path="/remarks/node()"/>
    /// <para>
    /// Translated into Ignixa's representation - Firely surfaces the temporal primitives as
    /// <c>Hl7.Fhir.ElementModel.Types</c> instances; <see cref="FirelyPrimitiveValues.ToIgnixa"/>
    /// translates them to <see cref="FhirTemporal"/> so Ignixa's evaluators can handle them.
    /// Memoized so that repeated reads return the same instance rather than re-rendering the
    /// wire-format string each time. The value is captured on first read: this adapter is a
    /// snapshot of the wrapped element, not a live view of it, so re-create it after mutating the
    /// element underneath. The resolved flag is <c>volatile</c> so a concurrent reader cannot see
    /// it set before the value it guards.
    /// </para>
    /// </remarks>
    public object? Value
    {
        get
        {
            if (!_translatedValueResolved)
            {
                _translatedValue = FirelyPrimitiveValues.ToIgnixa(_firelyElement.Value, _firelyElement.InstanceType);
                _translatedValueResolved = true;
            }

            return _translatedValue;
        }
    }

    /// <inheritdoc/>
    public string InstanceType => _firelyElement.InstanceType ?? string.Empty;

    /// <inheritdoc/>
    public string Location => _firelyElement.Location;

    /// <inheritdoc/>
    /// <remarks>
    /// Derived from <see cref="Value"/> rather than read live off the wrapped element, so the two
    /// cannot disagree once the value has been snapshotted. Translation never maps a non-null
    /// value to null, so this is the same answer the wrapped element would give.
    /// </remarks>
    public bool HasPrimitiveValue => Value is not null;

    /// <inheritdoc/>
    public IType? Type => _firelyElement.Definition != null
        ? new TypeAdapter(_firelyElement.Definition, InstanceType)
        : null;

    /// <inheritdoc/>
    /// <remarks>
    /// A named lookup keeps only children whose <see cref="Name"/> matches exactly. Choice children
    /// are exposed by their base name, so <c>Children("value")</c> returns a <c>valueQuantity</c>
    /// child named <c>value</c>, and <c>Children("valueQuantity")</c> returns nothing.
    /// <para>
    /// A named lookup always asks the wrapped element for that name only, so an element the model
    /// does not define does not break a lookup of a modelled sibling, and the result does not depend
    /// on whether <c>Children()</c> ran first. An undefined sibling that starts with a choice name,
    /// such as <c>valueFoo</c>, makes a lookup of that choice (<c>Children("value")</c>) throw. The
    /// undefined element itself follows Firely: 5.x never returns it by name, nor, in Passthrough
    /// mode, anything inside it; 6.x returns both when no settings are passed or in Passthrough mode,
    /// and throws in Report mode.
    /// </para>
    /// <para>
    /// Results are cached. A child reached both by name and by enumeration is the same adapter
    /// instance when both lookups list that name's children at the same <see cref="Location"/>s in
    /// the same order; otherwise each lookup keeps its own adapters with the same values.
    /// </para>
    /// </remarks>
    public IReadOnlyList<IElement> Children(string? name = null)
    {
        if (name == null)
        {
            return _cachedAllChildren ??= WrapAllChildren();
        }

        _childrenByName ??= new Dictionary<string, IReadOnlyList<IElement>>();

        if (!_childrenByName.TryGetValue(name, out var named))
        {
            named = WrapChildrenNamed(name);
            _childrenByName[name] = named;
        }

        return named;
    }

    private IElement[] WrapChildrenNamed(string name)
    {
        // Enumerating every child makes Firely throw in Report mode, the 5.x default, for any element
        // its model does not define, even when the caller wants a modelled sibling, so ask for this
        // name alone. The Name check keeps the match exact, because Firely 5 matches choice names by
        // prefix ("amount" also returns "amountType") and Firely 6 also resolves suffixed choice
        // names such as "valueDateTime".
        var children = _firelyElement.Children(name).Where(child => child.Name == name).ToArray();

        if (_cachedAllChildren?.Where(child => child.Name == name).ToArray() is { } enumerated
            && HaveSameLocations(children, enumerated))
        {
            return enumerated;
        }

        return Array.ConvertAll(children, Wrap);
    }

    private IElement[] WrapAllChildren()
    {
        var children = _firelyElement.Children().ToArray();
        var reusable = ReusableNamedChildren(children);

        return Array.ConvertAll(children, child =>
            reusable.TryGetValue(child.Name, out var named) ? named.Dequeue() : Wrap(child));
    }

    // The cached named lookups that can stand in for these children, queued in document order.
    private Dictionary<string, Queue<IElement>> ReusableNamedChildren(Hl7.Fhir.ElementModel.ITypedElement[] children)
    {
        var reusable = new Dictionary<string, Queue<IElement>>();

        if (_childrenByName is null)
        {
            return reusable;
        }

        var enumeratedByName = children.ToLookup(child => child.Name);
        foreach (var (name, named) in _childrenByName)
        {
            if (HaveSameLocations(enumeratedByName[name], named))
            {
                reusable[name] = new Queue<IElement>(named);
            }
        }

        return reusable;
    }

    // subsetOf() and supersetOf() accept the same instance before comparing children, which can throw
    // for an undefined child, yet Firely wraps children afresh on every call, so a child reached by
    // name and by enumeration should be one adapter. Both lookups list a name's children in document
    // order, so they pair up by position, and Location confirms every pair. When the Locations
    // disagree, for example because Firely numbers a run of siblings split by another element
    // differently in each, the pairing cannot be confirmed, so nothing is shared.
    private static bool HaveSameLocations(
        IEnumerable<Hl7.Fhir.ElementModel.ITypedElement> children,
        IEnumerable<IElement> adapters) =>
        children.Select(child => child.Location).SequenceEqual(adapters.Select(adapter => adapter.Location));

    private static IElement Wrap(Hl7.Fhir.ElementModel.ITypedElement child) => new IgnixaElementAdapter(child);

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
        private readonly string _instanceType;
        private TypeInfo? _cachedInfo;

        public TypeAdapter(FirelyElementDef definition, string instanceType)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _instanceType = instanceType;
        }

        public TypeInfo Info
        {
            get
            {
                if (_cachedInfo.HasValue)
                    return _cachedInfo.Value;

                var selectedChoiceType = _definition.IsChoiceElement
                    ? _definition.Type.FirstOrDefault(type =>
                        string.Equals(type.GetTypeName(), _instanceType, StringComparison.Ordinal))
                    : null;

                // A custom ITypedElement can report an InstanceType absent from its declared choice types.
                // The definition remains the authority for declared metadata, so preserve the prior first-type
                // fallback instead of fabricating a declaration from the unmatched runtime value.
                var typeName = (selectedChoiceType ?? _definition.Type.FirstOrDefault())?.GetTypeName()
                    ?? _definition.ElementName;
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
