// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// FHIR Condition clinical status codes.
/// </summary>
public static class ConditionClinicalStatus
{
    /// <summary>The subject is currently experiencing the condition</summary>
    public const string Active = "active";

    /// <summary>The subject is no longer experiencing the condition</summary>
    public const string Resolved = "resolved";

    /// <summary>The subject is not presently experiencing the condition</summary>
    public const string Inactive = "inactive";

    /// <summary>The condition is temporarily controlled but may return</summary>
    public const string Remission = "remission";

    /// <summary>The condition was entered in error</summary>
    public const string EnteredInError = "entered-in-error";

    /// <summary>
    /// Returns the closest clinical status code that is valid for <paramref name="version"/>.
    /// Ballot4 R6 pruned the condition-clinical valueset down to active/inactive/unknown, dropping
    /// resolved/recurrence/relapse/remission. For R6 those removed codes are substituted with
    /// "inactive" (the closest surviving code); every other FHIR version's code is returned unchanged.
    /// </summary>
    /// <param name="status">The requested clinical status code.</param>
    /// <param name="version">The target FHIR version.</param>
    /// <returns>A clinical status code valid for <paramref name="version"/>.</returns>
    public static string ForVersion(string status, FhirVersion version)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (version != FhirVersion.R6)
        {
            return status;
        }

        return status switch
        {
            Resolved or Remission or "recurrence" or "relapse" => Inactive,
            _ => status
        };
    }
}
