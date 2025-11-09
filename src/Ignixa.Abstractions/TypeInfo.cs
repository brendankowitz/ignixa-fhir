// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Strongly-typed type information (struct for stack allocation).
/// Optimized for fast type checking and minimal memory footprint.
/// </summary>
/// <remarks>
/// This struct is a value type that lives on the stack, providing zero GC pressure
/// for type metadata operations. Use this for high-performance scenarios where
/// type information needs to be passed around frequently.
/// </remarks>
public readonly struct TypeInfo : IEquatable<TypeInfo>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TypeInfo"/> struct.
    /// </summary>
    /// <param name="name">FHIR type name (e.g., "HumanName", "string", "Patient").</param>
    /// <param name="primitive">Primitive type classification. Use <see cref="FhirPrimitive.None"/> for complex types.</param>
    /// <param name="isResource">True if this is a FHIR resource type.</param>
    /// <param name="isAbstract">True if this is an abstract type that cannot be instantiated directly.</param>
    /// <param name="isChoiceElement">True if this is a choice element (value[x]).</param>
    /// <param name="isModifier">True if this modifies the meaning of other elements.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
    public TypeInfo(
        string name,
        FhirPrimitive primitive = FhirPrimitive.None,
        bool isResource = false,
        bool isAbstract = false,
        bool isChoiceElement = false,
        bool isModifier = false)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Primitive = primitive;
        IsResource = isResource;
        IsAbstract = isAbstract;
        IsChoiceElement = isChoiceElement;
        IsModifier = isModifier;
    }

    /// <summary>
    /// FHIR type name (e.g., "HumanName", "string", "Patient").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Primitive type (byte enum for ~2ns type checking).
    /// <see cref="FhirPrimitive.None"/> for complex types.
    /// </summary>
    public FhirPrimitive Primitive { get; }

    /// <summary>
    /// True if this is a FHIR resource type (e.g., Patient, Observation).
    /// </summary>
    public bool IsResource { get; }

    /// <summary>
    /// True if this is an abstract type (e.g., Resource, DomainResource).
    /// Abstract types cannot be instantiated directly.
    /// </summary>
    public bool IsAbstract { get; }

    /// <summary>
    /// True if this is a choice element (value[x]).
    /// Used for polymorphic navigation in Children().
    /// </summary>
    public bool IsChoiceElement { get; }

    /// <summary>
    /// True if this modifies the meaning of other elements.
    /// Required for validation (Tier 3 - modifier element checks).
    /// </summary>
    public bool IsModifier { get; }

    /// <summary>
    /// Fast primitive type check (~2ns vs ~45ns string comparison).
    /// </summary>
    public bool IsPrimitive => Primitive != FhirPrimitive.None;

    /// <summary>
    /// Determines whether the specified <see cref="TypeInfo"/> is equal to the current <see cref="TypeInfo"/>.
    /// </summary>
    /// <param name="other">The <see cref="TypeInfo"/> to compare with the current instance.</param>
    /// <returns>True if the specified <see cref="TypeInfo"/> is equal to the current instance; otherwise, false.</returns>
    public bool Equals(TypeInfo other) =>
        Name == other.Name &&
        Primitive == other.Primitive &&
        IsResource == other.IsResource;

    /// <summary>
    /// Determines whether the specified object is equal to the current <see cref="TypeInfo"/>.
    /// </summary>
    /// <param name="obj">The object to compare with the current instance.</param>
    /// <returns>True if the specified object is equal to the current instance; otherwise, false.</returns>
    public override bool Equals(object? obj) =>
        obj is TypeInfo other && Equals(other);

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Name, Primitive, IsResource);

    /// <summary>
    /// Determines whether two specified instances of <see cref="TypeInfo"/> are equal.
    /// </summary>
    public static bool operator ==(TypeInfo left, TypeInfo right) => left.Equals(right);

    /// <summary>
    /// Determines whether two specified instances of <see cref="TypeInfo"/> are not equal.
    /// </summary>
    public static bool operator !=(TypeInfo left, TypeInfo right) => !left.Equals(right);

    /// <summary>
    /// Returns a string that represents the current <see cref="TypeInfo"/>.
    /// </summary>
    /// <returns>A string representation of the type information.</returns>
    public override string ToString() =>
        IsPrimitive ? $"{Name} (primitive: {Primitive})" : Name;
}
