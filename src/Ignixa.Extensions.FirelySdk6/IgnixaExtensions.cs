// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Ignixa.Abstractions;

namespace Ignixa.Extensions.FirelySdk;

/// <summary>
/// Extension methods for converting Firely SDK types to Ignixa types.
/// </summary>
public static class IgnixaExtensions
{
    /// <summary>
    /// Converts a Firely SDK ITypedElement to Ignixa IElement.
    /// </summary>
    /// <param name="element">Firely SDK typed element</param>
    /// <returns>Ignixa element adapter</returns>
    /// <remarks>
    /// This method enables using Firely SDK types with Ignixa-based libraries.
    /// The adapter lazily materializes children to avoid allocation overhead.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Convert Firely element to Ignixa
    /// ITypedElement firelyElement = ...;
    /// IElement ignixaElement = firelyElement.ToIgnixaElement();
    ///
    /// // Now use with Ignixa libraries
    /// var validator = new IgnixaValidator();
    /// var result = validator.Validate(ignixaElement);
    /// </code>
    /// </example>
    public static IElement ToIgnixaElement(this Hl7.Fhir.ElementModel.ITypedElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        // If already an Ignixa element wrapped in TypedElementAdapter, unwrap it
        if (element is TypedElementAdapter adapter)
        {
            var unwrapped = adapter.Annotation<IElement>();
            if (unwrapped != null)
                return unwrapped;
        }

        return new IgnixaElementAdapter(element);
    }

    /// <summary>
    /// Converts multiple Firely SDK ITypedElements to Ignixa IElements.
    /// </summary>
    /// <param name="elements">Firely SDK typed elements</param>
    /// <returns>Ignixa element adapters</returns>
    public static IEnumerable<IElement> ToIgnixaElements(this IEnumerable<Hl7.Fhir.ElementModel.ITypedElement> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        return elements.Select(e => e.ToIgnixaElement());
    }
}
