// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// The <c>patient_list</c> filter: whitespace-separated terms that must each occur, ignoring case, in the patient's id, its
/// listed name, one of its identifier values (an MRN, say) or its birthDate. A term is a substring, so <c>Madalyn</c>,
/// <c>1994-12-29</c> and the first characters of an id all match. A term with a <c>|</c> is an identifier token as FHIR
/// search writes it, <c>system|value</c> (or <c>|value</c> for an identifier with no system), and matches an identifier exactly.
/// </summary>
internal sealed class PatientMatch
{
    private readonly string[] _terms;

    private PatientMatch(string[] terms) => _terms = terms;

    /// <exception cref="ToolRequestException">The query is blank or longer than <see cref="PatientLister.MaxQueryLength"/>.</exception>
    public static PatientMatch Parse(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        string[] terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0 || query.Length > PatientLister.MaxQueryLength)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'query' must be a name, identifier value (an MRN), id or birth date of 1 to {PatientLister.MaxQueryLength} characters, for example 'Madalyn' or '1994-12-29'.");
        }

        return new PatientMatch(terms);
    }

    /// <summary>Whether every term occurs in the patient's id, name, identifier values or birthDate, or names one of its identifiers.</summary>
    public bool Matches(string id, string? name, IElement patient)
    {
        if (_terms.Any(term => term.Contains('|', StringComparison.Ordinal)))
        {
            List<(string? System, string? Value)> identifiers = [.. patient.Select("identifier").Select(identifier => (identifier.Scalar("system") as string, identifier.Scalar("value") as string))];
            if (!_terms.Where(term => term.Contains('|', StringComparison.Ordinal)).All(term => NamesIdentifier(term, identifiers)))
            {
                return false;
            }
        }

        string[] fields =
        [
            id,
            .. name is null ? [] : new[] { name },
            .. ClinicalValues.Strings(patient, "identifier.value"),
            .. ClinicalValues.Literal(patient, "birthDate") is { } birthDate ? new[] { birthDate } : [],
        ];
        return _terms
            .Where(term => !term.Contains('|', StringComparison.Ordinal))
            .All(term => fields.Any(field => field.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool NamesIdentifier(string token, List<(string? System, string? Value)> identifiers)
    {
        int bar = token.IndexOf('|', StringComparison.Ordinal);
        string system = token[..bar];
        string value = token[(bar + 1)..];
        return identifiers.Any(identifier => identifier.Value == value && (system.Length == 0 ? string.IsNullOrEmpty(identifier.System) : identifier.System == system));
    }
}
