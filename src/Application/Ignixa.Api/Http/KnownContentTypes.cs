// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Api.Http;

/// <summary>
/// Well-known content type strings used across the FHIR API.
/// Provides a centralized location for media type constants.
/// </summary>
public static class KnownContentTypes
{
    /// <summary>
    /// FHIR-specific JSON content type: application/fhir+json
    /// </summary>
    public const string ApplicationFhirJson = "application/fhir+json";

    /// <summary>
    /// FHIR-specific JSON content type with UTF-8 charset: application/fhir+json; charset=utf-8
    /// </summary>
    public const string ApplicationFhirJsonUtf8 = "application/fhir+json; charset=utf-8";

    /// <summary>
    /// Standard JSON content type: application/json
    /// </summary>
    public const string ApplicationJson = "application/json";
}
