// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>A coded value of a draft, as the input gave it, and where in the resource it went; its terminology is checked.</summary>
/// <param name="Element">Its path in the resource, for example <c>MedicationRequest.medicationCodeableConcept.coding[0]</c>.</param>
/// <param name="System">The code system (an absolute URI).</param>
/// <param name="Code">The code.</param>
/// <param name="Display">The display given, checked against the code system's; null when none was given.</param>
internal sealed record DraftCoding(string Element, string System, string Code, string? Display)
{
    /// <summary>
    /// Reads <c>{system, code, display?}</c> from <paramref name="input"/>; the system must be an absolute URI. Records where it
    /// goes as <paramref name="element"/>.
    /// </summary>
    public static DraftCoding Read(DraftArguments input, string element)
    {
        ArgumentNullException.ThrowIfNull(input);

        string system = input.RequiredString("system");
        string code = input.RequiredString("code");
        string? display = input.OptionalString("display");
        input.Done();
        if (!Uri.TryCreate(system, UriKind.Absolute, out _) || system.Any(char.IsWhiteSpace))
        {
            throw DraftArguments.Invalid($"'{input.Of("system")}' must be an absolute URI, the code system's canonical url (for example http://loinc.org).");
        }

        return new DraftCoding(element, system, code, display);
    }

    /// <summary>The Coding.</summary>
    public JsonObject ToCoding()
    {
        var coding = new JsonObject { ["system"] = System, ["code"] = Code };
        if (Display is not null)
        {
            coding["display"] = Display;
        }

        return coding;
    }

    /// <summary>A CodeableConcept holding this one Coding.</summary>
    public JsonObject ToCodeableConcept() => new() { ["coding"] = new JsonArray(ToCoding()) };
}
