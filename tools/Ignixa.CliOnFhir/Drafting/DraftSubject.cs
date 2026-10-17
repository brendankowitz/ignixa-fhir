// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>The members every draft takes: the patient it is for, the encounter it belongs to, and a note.</summary>
/// <param name="Patient">The patient's id.</param>
/// <param name="Encounter">The encounter's id; null when none was given.</param>
/// <param name="Note">The note's text; null when none was given. It is resource content, never repeated in a message.</param>
internal sealed record DraftSubject(string Patient, string? Encounter, string? Note)
{
    /// <summary>Reads <c>patient</c> (<c>Patient/&lt;id&gt;</c> or the id), <c>encounter</c> (likewise) and <c>note</c>.</summary>
    public static DraftSubject Read(DraftArguments input)
    {
        ArgumentNullException.ThrowIfNull(input);

        string patient = IdOf(input, "patient", "Patient", input.RequiredString("patient"));
        string? encounter = input.OptionalString("encounter") is { } text ? IdOf(input, "encounter", "Encounter", text) : null;
        return new DraftSubject(patient, encounter, input.OptionalString("note"));
    }

    /// <summary>The references to check: the patient, then the encounter.</summary>
    public IEnumerable<DraftReference> References(string resourceType)
    {
        yield return new DraftReference($"{resourceType}.subject", "Patient", Patient, IsSubject: true);
        if (Encounter is not null)
        {
            yield return new DraftReference($"{resourceType}.encounter", "Encounter", Encounter, IsSubject: false);
        }
    }

    /// <summary>Adds <c>subject</c> and, when given, <c>encounter</c>.</summary>
    public void AddSubject(JsonObject resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        resource["subject"] = new JsonObject { ["reference"] = $"Patient/{Patient}" };
        if (Encounter is not null)
        {
            resource["encounter"] = new JsonObject { ["reference"] = $"Encounter/{Encounter}" };
        }
    }

    /// <summary>Adds <c>note</c> when given.</summary>
    public void AddNote(JsonObject resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (Note is not null)
        {
            resource["note"] = new JsonArray(new JsonObject { ["text"] = Note });
        }
    }

    /// <summary>The id of a reference to <paramref name="type"/> written <c>Type/id</c> or as the id alone.</summary>
    public static string IdOf(DraftArguments input, string name, string type, string text)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(text);

        string id = text.StartsWith(type + "/", StringComparison.Ordinal) ? text[(type.Length + 1)..] : text;
        return FhirIds.IsValid(id) ? id : throw DraftArguments.Invalid($"'{input.Of(name)}' must be {type}/<id> or the id, a FHIR id (1 to 64 letters, digits, '-' or '.').");
    }
}
