// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>Values the patient tools read from a resource with FHIRPath, by <see cref="ClinicalRules"/>.</summary>
internal static class ClinicalValues
{
    /// <summary>
    /// The resource's main clinical date (<see cref="ClinicalRules.MainDate"/>): the first expression yielding a valid date;
    /// null for a type without one or when none yields a date. An unparseable date literal is not a date.
    /// </summary>
    public static FhirTemporal? MainDate(string resourceType, IElement resource) =>
        ClinicalRules.MainDate.TryGetValue(resourceType, out IReadOnlyList<string>? expressions)
            ? expressions.Select(expression => resource.Select(expression).Select(element => element.Value).OfType<FhirTemporal>().FirstOrDefault()).FirstOrDefault(date => date is not null)
            : null;

    /// <summary>The resource's status code as items show it (<see cref="ClinicalRules.StatusCode"/>, else <c>status</c>).</summary>
    public static string? Status(string resourceType, IElement resource) =>
        resource.Select(ClinicalRules.StatusCode.GetValueOrDefault(resourceType, "status")).Select(element => element.Value).OfType<string>().FirstOrDefault();

    /// <summary>
    /// The Patient's display name: <see cref="ClinicalRules.PatientName"/>'s first match, as its given names then family. The
    /// name's free <c>text</c> is never used.
    /// </summary>
    public static string? PatientName(IElement patient)
    {
        IElement? name = ClinicalRules.PatientName.Select(expression => patient.Select(expression).FirstOrDefault()).FirstOrDefault(found => found is not null);
        if (name is null)
        {
            return null;
        }

        List<string> parts = [.. name.Select("given").Select(element => element.Value).OfType<string>(), .. name.Select("family").Select(element => element.Value).OfType<string>()];
        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    /// <summary>A primitive's value as its FHIR literal (a date's literal, a code), or null.</summary>
    public static string? Literal(IElement resource, string expression) =>
        resource.Select(expression).Select(element => element.Value switch
        {
            FhirTemporal date => date.Literal,
            string value => value,
            bool flag => flag ? "true" : "false",
            _ => null,
        }).FirstOrDefault(value => value is not null);
}
