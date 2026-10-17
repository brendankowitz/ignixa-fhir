// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Clock;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>
/// A draft Observation (R4) from its typed input: <c>patient</c>, <c>encounter?</c>, <c>note?</c>, <c>code</c> <c>{system, code,
/// display?}</c>, <c>status</c> (default <c>preliminary</c>: an Observation has no <c>draft</c> status), <c>category?</c> (a coding),
/// <c>value?</c> (exactly one of <c>quantity</c> <c>{value, unit?, system?, code?}</c>, <c>codeable</c> <c>{system, code,
/// display?}</c> or <c>string</c>), <c>effective</c> (a dateTime) and <c>issued</c> (an instant), both defaulting to the session's instant.
/// </summary>
internal static class ObservationDraft
{
    public const string ResourceType = "Observation";

    public static DraftContent Build(DraftArguments input, AsOf asOf)
    {
        ArgumentNullException.ThrowIfNull(input);

        DraftSubject subject = DraftSubject.Read(input);
        DraftCoding code = DraftCoding.Read(input.RequiredObject("code"), $"{ResourceType}.code.coding[0]", primary: true);
        string status = input.OptionalString("status") ?? "preliminary";
        DraftArguments? categoryInput = input.OptionalObject("category");
        DraftCoding? category = categoryInput is null ? null : DraftCoding.Read(categoryInput, $"{ResourceType}.category[0].coding[0]");
        (string Member, JsonNode Node, DraftCoding? Coding)? value = input.OptionalObject("value") is { } valueInput ? Value(valueInput) : null;
        string effective = DraftTimes.DateTimeOr(input, "effective", asOf);
        string issued = DraftTimes.InstantOr(input, "issued", asOf);
        input.Done();

        var resource = new JsonObject
        {
            ["resourceType"] = ResourceType,
            ["status"] = status,
        };
        if (category is not null)
        {
            resource["category"] = new JsonArray(category.ToCodeableConcept());
        }

        resource["code"] = code.ToCodeableConcept();
        subject.AddSubject(resource);
        resource["effectiveDateTime"] = effective;
        resource["issued"] = issued;
        if (value is var (member, node, _))
        {
            resource[member] = node;
        }

        subject.AddNote(resource);
        List<DraftCoding> codings = [];
        if (category is not null)
        {
            codings.Add(category);
        }

        codings.Add(code);
        if (value?.Coding is { } valueCoding)
        {
            codings.Add(valueCoding);
        }

        return new DraftContent(resource, codings, [.. subject.References(ResourceType)]);
    }

    // The value[x] member and its content: exactly one variant may be given.
    private static (string Member, JsonNode Node, DraftCoding? Coding) Value(DraftArguments input)
    {
        DraftArguments? quantityInput = input.OptionalObject("quantity");
        DraftArguments? codeableInput = input.OptionalObject("codeable");
        string? text = input.OptionalString("string");
        input.Done();
        int given = (quantityInput is null ? 0 : 1) + (codeableInput is null ? 0 : 1) + (text is null ? 0 : 1);
        if (given != 1)
        {
            throw DraftArguments.Invalid($"{input.Path} must give exactly one of 'quantity', 'codeable' or 'string', not {given}.");
        }

        if (quantityInput is not null)
        {
            DraftQuantity quantity = DraftQuantity.Read(quantityInput, "system");
            return ("valueQuantity", quantity.ToQuantity(), quantity.Coding($"{ResourceType}.valueQuantity"));
        }

        if (codeableInput is not null)
        {
            DraftCoding coding = DraftCoding.Read(codeableInput, $"{ResourceType}.valueCodeableConcept.coding[0]", primary: true);
            return ("valueCodeableConcept", coding.ToCodeableConcept(), coding);
        }

        return ("valueString", JsonValue.Create(text!), null);
    }
}
