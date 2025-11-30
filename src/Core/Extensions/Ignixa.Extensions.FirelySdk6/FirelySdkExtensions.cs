// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Extensions.FirelySdk;

/// <summary>
/// Extension methods for converting Ignixa types to Firely SDK types.
/// </summary>
public static class FirelySdkExtensions
{
    /// <summary>
    /// Converts an Ignixa IElement to Firely SDK ITypedElement.
    /// </summary>
    /// <param name="element">Ignixa element</param>
    /// <returns>Firely SDK typed element adapter</returns>
    /// <remarks>
    /// This method enables using Ignixa types with Firely SDK-based tools
    /// (e.g., Hl7.FhirPath, Firely Validator).
    /// </remarks>
    /// <example>
    /// <code>
    /// // Convert Ignixa element to Firely
    /// IElement ignixaElement = ...;
    /// ITypedElement firelyElement = ignixaElement.ToTypedElement();
    ///
    /// // Now use with Firely SDK tools
    /// var navigator = firelyElement.ToFhirPathNavigator();
    /// var result = navigator.Scalar("Patient.name.family");
    /// </code>
    /// </example>
    public static Hl7.Fhir.ElementModel.ITypedElement ToTypedElement(this IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        // If already a Firely element wrapped in IgnixaElementAdapter, unwrap it
        if (element is IgnixaElementAdapter adapter)
        {
            var unwrapped = adapter.Meta<Hl7.Fhir.ElementModel.ITypedElement>();
            if (unwrapped != null)
                return unwrapped;
        }

        return new TypedElementAdapter(element);
    }

    /// <summary>
    /// Converts multiple Ignixa IElements to Firely SDK ITypedElements.
    /// </summary>
    /// <param name="elements">Ignixa elements</param>
    /// <returns>Firely SDK typed element adapters</returns>
    public static IEnumerable<Hl7.Fhir.ElementModel.ITypedElement> ToTypedElements(this IEnumerable<IElement> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        return elements.Select(e => e.ToTypedElement());
    }

    /// <summary>
    /// Converts a read-only list of Ignixa IElements to Firely SDK ITypedElements.
    /// </summary>
    /// <param name="elements">Ignixa elements as read-only list</param>
    /// <returns>Firely SDK typed element adapters</returns>
    public static IEnumerable<Hl7.Fhir.ElementModel.ITypedElement> ToTypedElements(this IReadOnlyList<IElement> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        return elements.Select(e => e.ToTypedElement());
    }

    /// <summary>
    /// Converts a Firely SDK ISourceNode to Ignixa ISourceNavigator.
    /// </summary>
    /// <param name="sourceNode">Firely SDK source node</param>
    /// <returns>Ignixa source navigator adapter</returns>
    /// <remarks>
    /// This adapter bridges Firely's ISourceNode to Ignixa's ISourceNavigator.
    /// For schema-aware navigation, use <see cref="ToElement(Hl7.Fhir.ElementModel.ISourceNode, ISchema)"/> instead.
    /// </remarks>
    public static ISourceNavigator ToSourceNavigator(this Hl7.Fhir.ElementModel.ISourceNode sourceNode)
    {
        ArgumentNullException.ThrowIfNull(sourceNode);

        return new SourceNavigatorAdapter(sourceNode);
    }

    /// <summary>
    /// Converts a Firely SDK ISourceNode to Ignixa IElement with schema metadata.
    /// </summary>
    /// <param name="sourceNode">Firely SDK source node</param>
    /// <param name="schema">Ignixa schema provider for type metadata</param>
    /// <returns>Schema-aware Ignixa IElement</returns>
    /// <remarks>
    /// <para>
    /// This method enables using Firely SDK-parsed FHIR data with Ignixa's
    /// schema-aware element navigation, providing type metadata from the specified schema.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Parse with Firely SDK
    /// ISourceNode firelyNode = FhirJsonNode.Parse(json);
    ///
    /// // Convert directly to schema-aware IElement
    /// IElement element = firelyNode.ToElement(schema);
    ///
    /// // Access type-aware properties
    /// var instanceType = element.InstanceType;  // e.g., "Patient"
    /// var type = element.Type;                   // IType with schema metadata
    /// </code>
    /// </example>
    public static IElement ToElement(this Hl7.Fhir.ElementModel.ISourceNode sourceNode, ISchema schema)
    {
        ArgumentNullException.ThrowIfNull(sourceNode);
        ArgumentNullException.ThrowIfNull(schema);

        var navigator = new SourceNavigatorAdapter(sourceNode);
        return navigator.ToElement(schema);
    }
}
