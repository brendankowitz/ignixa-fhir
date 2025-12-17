// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.NarrativeGenerator;

/// <summary>
/// Generates XHTML narrative for FHIR resources using Scriban templates.
/// </summary>
public interface INarrativeGenerator
{
    /// <summary>
    /// Generates XHTML narrative for a FHIR resource.
    /// </summary>
    /// <param name="resource">The FHIR resource to generate narrative for.</param>
    /// <param name="culture">Optional culture for localized strings. Defaults to <see cref="CultureInfo.CurrentCulture"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated XHTML narrative as a string.</returns>
    Task<string> GenerateNarrativeAsync(
        ResourceJsonNode resource,
        CultureInfo? culture = null,
        CancellationToken cancellationToken = default);
}
