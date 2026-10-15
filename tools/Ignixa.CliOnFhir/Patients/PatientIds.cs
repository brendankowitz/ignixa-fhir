// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>The <c>patientId</c> argument of the context tools.</summary>
internal static partial class PatientIds
{
    /// <exception cref="ToolRequestException"><paramref name="patientId"/> is not a FHIR id.</exception>
    public static void Validate(string? patientId)
    {
        if (patientId is null || !FhirId().IsMatch(patientId))
        {
            throw ToolRequestException.Error("invalid-parameter", $"'patientId' must be a Patient id (1 to 64 letters, digits, '-' or '.'), for example one from patient_list, not '{patientId}'.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9\\-\\.]{1,64}$")]
    private static partial Regex FhirId();
}
