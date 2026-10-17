// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Clock;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>
/// A draft MedicationRequest (R4) from its typed input: <c>patient</c>, <c>encounter?</c>, <c>note?</c>, <c>medication</c>
/// <c>{system, code, display?}</c>, <c>status</c> (default <c>draft</c>), <c>intent</c> (default <c>order</c>), <c>authoredOn</c>
/// (default the session's instant), and one dosage instruction from <c>dosageText?</c>, <c>dose?</c> <c>{value, unit?, unitSystem?,
/// code?}</c>, <c>route?</c> <c>{system, code, display?}</c> and <c>frequency?</c> <c>{count, period, periodUnit}</c> (<c>count</c>
/// times per <c>period</c> <c>periodUnit</c>: Timing.repeat.frequency, period and periodUnit).
/// </summary>
internal static class MedicationRequestDraft
{
    public const string ResourceType = "MedicationRequest";

    public static DraftContent Build(DraftArguments input, AsOf asOf)
    {
        ArgumentNullException.ThrowIfNull(input);

        DraftSubject subject = DraftSubject.Read(input);
        DraftCoding medication = DraftCoding.Read(input.RequiredObject("medication"), $"{ResourceType}.medicationCodeableConcept.coding[0]");
        string status = input.OptionalString("status") ?? "draft";
        string intent = input.OptionalString("intent") ?? "order";
        string authoredOn = DraftTimes.DateTimeOr(input, "authoredOn", asOf);
        string? dosageText = input.OptionalString("dosageText");
        DraftArguments? doseInput = input.OptionalObject("dose");
        DraftQuantity? dose = doseInput is null ? null : DraftQuantity.Read(doseInput, "unitSystem");
        DraftArguments? routeInput = input.OptionalObject("route");
        DraftCoding? route = routeInput is null ? null : DraftCoding.Read(routeInput, $"{ResourceType}.dosageInstruction[0].route.coding[0]");
        JsonObject? repeat = input.OptionalObject("frequency") is { } frequency ? Repeat(frequency) : null;
        input.Done();

        var resource = new JsonObject
        {
            ["resourceType"] = ResourceType,
            ["status"] = status,
            ["intent"] = intent,
            ["medicationCodeableConcept"] = medication.ToCodeableConcept(),
        };
        subject.AddSubject(resource);
        resource["authoredOn"] = authoredOn;
        subject.AddNote(resource);
        if (Dosage(dosageText, dose, route, repeat) is { } dosage)
        {
            resource["dosageInstruction"] = new JsonArray(dosage);
        }

        List<DraftCoding> codings = [medication];
        if (route is not null)
        {
            codings.Add(route);
        }

        if (dose?.Coding($"{ResourceType}.dosageInstruction[0].doseAndRate[0].doseQuantity") is { } unit)
        {
            codings.Add(unit);
        }

        return new DraftContent(resource, codings, [.. subject.References(ResourceType)]);
    }

    private static JsonObject Repeat(DraftArguments frequency)
    {
        int count = frequency.RequiredPositiveInteger("count");
        decimal period = frequency.RequiredPositiveNumber("period");
        string periodUnit = frequency.RequiredString("periodUnit");
        frequency.Done();
        return new JsonObject { ["frequency"] = count, ["period"] = period, ["periodUnit"] = periodUnit };
    }

    private static JsonObject? Dosage(string? text, DraftQuantity? dose, DraftCoding? route, JsonObject? repeat)
    {
        if (text is null && dose is null && route is null && repeat is null)
        {
            return null;
        }

        var dosage = new JsonObject();
        if (text is not null)
        {
            dosage["text"] = text;
        }

        if (repeat is not null)
        {
            dosage["timing"] = new JsonObject { ["repeat"] = repeat };
        }

        if (route is not null)
        {
            dosage["route"] = route.ToCodeableConcept();
        }

        if (dose is not null)
        {
            dosage["doseAndRate"] = new JsonArray(new JsonObject { ["doseQuantity"] = dose.ToQuantity() });
        }

        return dosage;
    }
}
