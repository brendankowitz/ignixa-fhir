/*
 * Copyright (c) 2018, Firely (info@fire.ly) and contributors
 * See the file CONTRIBUTORS for details.
 *
 * This file is licensed under the BSD 3-Clause license
 * available at https://raw.githubusercontent.com/FirelyTeam/firely-net-sdk/master/LICENSE
 */

#nullable enable

namespace Ignixa.Abstractions;

/// <summary>
/// A element within a tree of typed FHIR data.
/// </summary>
/// <remarks>
/// This interface represents FHIR data as a tree of elements, including type information either present in
/// the instance or derived from fully aware of the FHIR definitions and types.
///
/// <para>
/// <strong>OBSOLETE:</strong> This interface is deprecated in favor of <see cref="IElement"/>.
/// The new interface provides better performance with <see cref="IReadOnlyList{T}"/> return types
/// and uses <see cref="TypeInfo"/> for strongly-typed metadata.
/// </para>
/// </remarks>
[Obsolete("Use IElement instead. ITypedElement will be removed in a future version. " +
          "Migrate by implementing IElement which uses IReadOnlyList<IElement> for Children() " +
          "and IType instead of IElementDefinitionSummary for type metadata.")]
#pragma warning disable CS0618 // Type or member is obsolete
public interface ITypedElement : IBaseElementNavigator<ITypedElement>
#pragma warning restore CS0618 // Type or member is obsolete
{
    /// <summary>
    /// An indication of the location of this node within the data represented by the <c>ITypedElement</c>.
    /// </summary>
    /// <remarks>The format of the location is the dotted name of the property, including indices to make
    /// sure repeated occurrences of an element can be distinguished. It needs to be sufficiently precise to aid
    /// the user in locating issues in the data.</remarks>
    string Location { get; }

    IElementDefinitionSummary? Definition { get; }
}
