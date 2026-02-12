// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Anonymizer;

/// <summary>
/// Settings for controlling anonymization behavior per request.
/// </summary>
public sealed record RequestOptions
{
    /// <summary>
    /// Whether to format output JSON with indentation.
    /// </summary>
    public bool IsPrettyOutput { get; init; }

    /// <summary>
    /// Whether to validate input FHIR resources before anonymization.
    /// </summary>
    public bool ValidateInput { get; init; }

    /// <summary>
    /// Whether to validate output FHIR resources after anonymization.
    /// </summary>
    public bool ValidateOutput { get; init; }
}
