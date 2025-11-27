// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Ignixa.Abstractions;

namespace Ignixa.Shims.FirelySdk;

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

        // If already a Firely element wrapped in CoreElementAdapter, unwrap it
        if (element is CoreElementAdapter adapter)
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
}
