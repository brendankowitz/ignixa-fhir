// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Diagnostics.CodeAnalysis;
using Ignixa.DeId.Configuration;

namespace Ignixa.DeId.Darts.Configuration;

/// <summary>
/// Provides built-in DARTS de-identification policy configurations.
/// </summary>
public static class BootstrapPolicies
{
    /// <summary>
    /// Creates the built-in configuration for a DARTS policy code. Its rules are server-defined, so their paths are
    /// evaluated in the server expression tier rather than the request-supplied one.
    /// </summary>
    /// <returns>False when <paramref name="policy"/> names no built-in policy.</returns>
    public static bool TryCreate(string policy, [NotNullWhen(true)] out DeIdOptions? options)
    {
        options = policy switch
        {
            DartsConstants.PolicySafeHarbor => CreateSafeHarborOptions(),
            DartsConstants.PolicyExpertDetermination => CreateExpertDeterminationOptions(),
            _ => null
        };

        return options is not null;
    }

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
                new FhirPathRule { IsServerDefined = true, Path = "Patient.id", Method = "cryptoHash" },
                new FhirPathRule { IsServerDefined = true, Path = "Patient.identifier", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "Patient.name", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "Patient.address", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "Patient.telecom", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "Patient.birthDate", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "Patient.photo", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "Patient.contact", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "Resource.text", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "descendants().ofType(date)", Method = "dateShift" },
                new FhirPathRule { IsServerDefined = true, Path = "descendants().ofType(dateTime)", Method = "dateShift" },
                new FhirPathRule { IsServerDefined = true, Path = "descendants().ofType(instant)", Method = "dateShift" },
                new FhirPathRule { IsServerDefined = true, Path = "descendants().ofType(Reference).display", Method = "redact" },
            ],
            Parameters = new ParameterOptions
            {
                EnablePartialDatesForRedact = true,
                EnablePartialAgesForRedact = true,
                EnablePartialZipCodesForRedact = true
            },
            Processing = new ProcessingOptions
            {
                ErrorHandling = ErrorHandlingMode.StopOnError
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
                new FhirPathRule { IsServerDefined = true, Path = "Patient.id", Method = "cryptoHash" },
                new FhirPathRule { IsServerDefined = true, Path = "Patient.identifier", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "Patient.name", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "Patient.address", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "Patient.telecom", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "Patient.birthDate", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "Patient.photo", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "Resource.text", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "descendants().ofType(date)", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "descendants().ofType(dateTime)", Method = "redact" },
                new FhirPathRule { IsServerDefined = true, Path = "descendants().ofType(Reference).display", Method = "redact" },
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
