// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>A Quantity of a draft: a value, the unit as shown, and optionally the coded unit (system and code), whose terminology is checked.</summary>
internal sealed record DraftQuantity(decimal Value, string? Unit, string? System, string? Code)
{
    /// <summary>
    /// Reads <c>{value, unit?, &lt;systemName&gt;?, code?}</c>. A code needs its system (FHIR qty-3), so a code without one is refused
    /// rather than given a system the caller did not name.
    /// </summary>
    /// <param name="systemName">The member that names the unit's system: <c>unitSystem</c> for a dose, <c>system</c> for a value.</param>
    public static DraftQuantity Read(DraftArguments input, string systemName)
    {
        ArgumentNullException.ThrowIfNull(input);

        decimal value = input.OptionalNumber("value") ?? throw DraftArguments.Invalid($"'{input.Of("value")}' is required.");
        string? unit = input.OptionalString("unit");
        string? system = input.OptionalString(systemName);
        string? code = input.OptionalString("code");
        input.Done();
        if (code is not null && system is null)
        {
            throw DraftArguments.Invalid($"'{input.Of("code")}' needs '{input.Of(systemName)}', the system the unit code is from (http://unitsofmeasure.org for UCUM): FHIR requires one with the other (qty-3).");
        }

        if (system is not null && !Uri.TryCreate(system, UriKind.Absolute, out _))
        {
            throw DraftArguments.Invalid($"'{input.Of(systemName)}' must be an absolute URI (for UCUM, http://unitsofmeasure.org).");
        }

        if (unit is null && code is null)
        {
            throw DraftArguments.Invalid($"{input.Path} needs 'unit' or 'code': a quantity without its unit cannot be read.");
        }

        return new DraftQuantity(value, unit, system, code);
    }

    /// <summary>The Quantity.</summary>
    public JsonObject ToQuantity()
    {
        var quantity = new JsonObject { ["value"] = Value };
        if (Unit is not null)
        {
            quantity["unit"] = Unit;
        }

        if (System is not null)
        {
            quantity["system"] = System;
        }

        if (Code is not null)
        {
            quantity["code"] = Code;
        }

        return quantity;
    }

    /// <summary>The coded unit as a coding to check, when the quantity has one.</summary>
    public DraftCoding? Coding(string element) => System is not null && Code is not null ? new DraftCoding(element, System, Code, Display: null) : null;
}
