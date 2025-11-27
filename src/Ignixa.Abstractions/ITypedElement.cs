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
/// Note: A new <see cref="IElement"/> interface is being developed for better performance
/// with <see cref="IReadOnlyList{T}"/> return types and <see cref="TypeInfo"/> for strongly-typed metadata.
/// </para>
/// </remarks>
#pragma warning disable CS0618 // IBaseElementNavigator is marked obsolete as internal warning but must be inherited for ITypedElement
public interface ITypedElement : IBaseElementNavigator<ITypedElement>
#pragma warning restore CS0618
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
