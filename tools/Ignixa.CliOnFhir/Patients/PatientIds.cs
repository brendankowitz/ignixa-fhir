// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>The <c>patientId</c> argument of the context tools.</summary>
internal static class PatientIds
{
    /// <summary>
    /// Refuses a <paramref name="patientId"/> that is neither a FHIR id nor the digest patient_list gives a Patient whose id
    /// is not one (<see cref="FhirIds"/>).
    /// </summary>
    /// <exception cref="ToolRequestException">The id has neither form.</exception>
    public static void Validate(string? patientId)
    {
        if (!FhirIds.IsValid(patientId) && !FhirIds.IsDigest(patientId))
        {
            throw ToolRequestException.Error("invalid-parameter", $"'patientId' must be a Patient id (1 to 64 letters, digits, '-' or '.') or the digest patient_list gives, for example one from patient_list, not '{QueryEcho.Value(patientId)}'.");
        }
    }
}
