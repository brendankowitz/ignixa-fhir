// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Concrete <see cref="IType"/> / <see cref="ITypeExtended"/> implementation produced by
/// <see cref="StructureDefinitionTypeAdapter"/>. Mirrors the shape of the per-version
/// <c>CoreType</c> classes emitted by codegen, but is reusable across FHIR versions and
/// usable from any assembly (the codegen <c>CoreType</c> is <c>private</c> per provider).
/// </summary>
internal sealed class AdaptedType : ITypeExtended
{
    private readonly IReadOnlyList<IType> _children;

    public AdaptedType(
        TypeInfo info,
        bool isCollection,
        bool isRequired,
        int order,
        int min,
        string max,
        IReadOnlyList<IType> children,
        IReadOnlyList<IConstraint> constraints,
        IBinding? binding,
        object? fixedValue,
        object? patternValue,
        IReadOnlyList<ITypeReference> types,
        string? defaultTypeName,
        IReadOnlyList<string> referenceTargets,
        string? contentReference,
        bool inSummary = false)
    {
        Info = info;
        IsCollection = isCollection;
        IsRequired = isRequired;
        InSummary = inSummary;
        Order = order;
        Min = min;
        Max = max ?? "*";
        _children = children ?? Array.Empty<IType>();
        Constraints = constraints ?? Array.Empty<IConstraint>();
        Binding = binding;
        FixedValue = fixedValue;
        PatternValue = patternValue;
        Types = types ?? Array.Empty<ITypeReference>();
        DefaultTypeName = defaultTypeName;
        ReferenceTargets = referenceTargets ?? Array.Empty<string>();
        ContentReference = contentReference;
    }

    public TypeInfo Info { get; }
    public bool IsCollection { get; }
    public bool IsRequired { get; }
    public bool InSummary { get; }
    public int Order { get; }
    public IReadOnlyList<IType> Children => _children;

    public int Min { get; }
    public string Max { get; }
    public IReadOnlyList<IConstraint> Constraints { get; }
    public IBinding? Binding { get; }
    public object? FixedValue { get; }
    public object? PatternValue { get; }
    public IReadOnlyList<ITypeReference> Types { get; }
    public string? DefaultTypeName { get; }
    public IReadOnlyList<string> ReferenceTargets { get; }
    public string? ContentReference { get; }
}
