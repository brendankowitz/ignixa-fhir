/*
 * Copyright (c) 2018, Firely (info@fire.ly) and contributors
 * See the file CONTRIBUTORS for details.
 *
 * This file is licensed under the BSD 3-Clause license
 * available at https://github.com/FirelyTeam/firely-net-sdk/blob/master/LICENSE
 */

namespace Ignixa.Abstractions;

/// <summary>
/// Navigates the raw FHIR wire format (JSON/XML) without type awareness.
/// </summary>
/// <remarks>
/// <para>This interface is typically implemented by parsers for FHIR serialization formats (JSON, XML).
/// It provides schema-less navigation where element names include type suffixes for choice elements
/// and all primitive values are represented as strings.</para>
/// <para>For type-enriched navigation with schema metadata, convert to <see cref="IElement"/> using
/// the ToElement extension method with a schema provider.</para>
/// <para>Implementations that report parsing errors should do so on the
/// <see cref="Children(string)"/> function and <see cref="Text"/> getter.</para>
/// </remarks>
public interface ISourceNavigator
{
    /// <summary>
    /// Gets the name of the node, e.g. "active", "valueQuantity".
    /// </summary>
    /// <remarks>Since the navigator has no type information, choice elements are represented as their
    /// name on the wire, including the type suffix (e.g., "valueQuantity" not "value").
    /// </remarks>
    string Name { get; }

    /// <summary>
    /// Gets the text of the primitive value of the node.
    /// </summary>
    /// <value>Returns the raw textual value as represented in the serialization, or null if there is no value in this node.</value>
    string Text { get; }

    /// <summary>
    /// Gets the location of this node within the tree of data.
    /// </summary>
    /// <value>A string of dot-separated names representing the path to the node within the tree, including indices
    /// to distinguish repeated occurrences of an element (e.g., "Patient.name[0].given[1]").</value>
    string Location { get; }

    /// <summary>
    /// Gets the FHIR resource type from the root JSON node (if present).
    /// </summary>
    /// <value>The value of the resourceType property in JSON, or null if not present or not at root level.</value>
    string ResourceType { get; }

    /// <summary>
    /// Enumerates the direct child nodes of the current node (if any).
    /// </summary>
    /// <param name="name">Optional. The name filter for the children. Can be omitted to not filter by name.</param>
    /// <returns>The children of the node matching the given filter, or all children if no filter was specified.
    /// If no children match the given filter, the function returns an empty enumerable.</returns>
    /// <remarks>
    /// <para>If the <paramref name="name"/> parameter ends in an asterisk ('*'),
    /// the function will return the children of which the name starts with the given name.</para>
    /// <para>Repeating elements will always be returned consecutively.</para>
    /// </remarks>
    IEnumerable<ISourceNavigator> Children(string? name = null);

    /// <summary>
    /// Retrieves metadata of the specified type attached to this node.
    /// </summary>
    /// <typeparam name="T">Metadata type to retrieve</typeparam>
    /// <returns>Metadata instance or null if not present</returns>
    T? Meta<T>() where T : class;
}
