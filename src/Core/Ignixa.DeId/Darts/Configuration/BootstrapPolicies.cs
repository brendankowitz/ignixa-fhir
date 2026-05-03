// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Configuration;

namespace Ignixa.DeId.Darts.Configuration;

/// <summary>
/// Provides built-in DARTS de-identification policy configurations.
/// </summary>
public static class BootstrapPolicies
{
    /// <summary>
    /// Creates the Safe Harbor de-identification policy configuration.
    /// </summary>
    public static DeIdOptions CreateSafeHarborOptions()
    {
        return new DeIdOptions
        {
            FhirVersion = "R4",
            Rules =
            [
                new FhirPathRule { Path = "Patient.id", Method = "cryptoHash" },
                new FhirPathRule { Path = "Patient.identifier", Method = "redact" },
                new FhirPathRule { Path = "Patient.name", Method = "redact" },
                new FhirPathRule { Path = "Patient.address", Method = "redact" },
                new FhirPathRule { Path = "Patient.telecom", Method = "redact" },
                new FhirPathRule { Path = "Patient.birthDate", Method = "redact" },
                new FhirPathRule { Path = "Patient.photo", Method = "redact" },
                new FhirPathRule { Path = "Patient.contact", Method = "redact" },
                new FhirPathRule { Path = "Resource.text", Method = "redact" },
                new FhirPathRule { Path = "descendants().ofType(date)", Method = "dateShift" },
                new FhirPathRule { Path = "descendants().ofType(dateTime)", Method = "dateShift" },
                new FhirPathRule { Path = "descendants().ofType(instant)", Method = "dateShift" },
                new FhirPathRule { Path = "descendants().ofType(Reference).display", Method = "redact" },
            ],
            Parameters = new ParameterOptions
            {
                EnablePartialDatesForRedact = true,
                EnablePartialAgesForRedact = true,
                EnablePartialZipCodesForRedact = true
            },
            Processing = new ProcessingOptions
            {
                ErrorHandling = ErrorHandlingMode.LogAndContinue
            }
        };
    }

    /// <summary>
    /// Creates the Expert Determination de-identification policy configuration.
    /// </summary>
    public static DeIdOptions CreateExpertDeterminationOptions()
    {
        return new DeIdOptions
        {
            FhirVersion = "R4",
            Rules =
            [
                new FhirPathRule { Path = "Patient.id", Method = "cryptoHash" },
                new FhirPathRule { Path = "Patient.identifier", Method = "redact" },
                new FhirPathRule { Path = "Patient.name", Method = "redact" },
                new FhirPathRule { Path = "Patient.address", Method = "redact" },
                new FhirPathRule { Path = "Patient.telecom", Method = "redact" },
                new FhirPathRule { Path = "Patient.birthDate", Method = "redact" },
                new FhirPathRule { Path = "Patient.photo", Method = "redact" },
                new FhirPathRule { Path = "Resource.text", Method = "redact" },
                new FhirPathRule { Path = "descendants().ofType(date)", Method = "redact" },
                new FhirPathRule { Path = "descendants().ofType(dateTime)", Method = "redact" },
                new FhirPathRule { Path = "descendants().ofType(Reference).display", Method = "redact" },
            ],
            Parameters = new ParameterOptions
            {
                EnablePartialDatesForRedact = false,
                EnablePartialAgesForRedact = false,
                EnablePartialZipCodesForRedact = false
            },
            Processing = new ProcessingOptions
            {
                ErrorHandling = ErrorHandlingMode.FailFast
            }
        };
    }
}
