// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>A concept as read, before the hierarchy is resolved; becomes a <see cref="Concept"/>.</summary>
internal sealed class RawConcept
{
    private RawConcept(string code, int order) => (Code, Order) = (code, order);

    public string Code { get; }

    public int Order { get; }

    public string? Display { get; private init; }

    public string? Definition { get; private init; }

    public string? Status { get; private set; }

    public bool Inactive { get; private set; }

    public List<ConceptProperty> Properties { get; } = [];

    public List<ConceptDesignation> Designations { get; } = [];

    public List<string> ParentCodes { get; } = [];

    public List<string> ChildCodes { get; } = [];

    public static RawConcept Read(JsonElement element, string code, int order, PropertyRoles roles)
    {
        var concept = new RawConcept(code, order)
        {
            Display = TerminologyJson.String(element, "display"),
            Definition = TerminologyJson.String(element, "definition"),
        };

        foreach (JsonElement designation in TerminologyJson.Array(element, "designation"))
        {
            if (TerminologyJson.String(designation, "value") is { } value)
            {
                JsonElement? use = TerminologyJson.Object(designation, "use");
                concept.Designations.Add(new ConceptDesignation(
                    TerminologyJson.NonEmpty(designation, "language"),
                    use is { } coding ? $"{TerminologyJson.String(coding, "system")}|{TerminologyJson.String(coding, "code")}" : null,
                    value));
            }
        }

        foreach (JsonElement property in TerminologyJson.Array(element, "property"))
        {
            if (TerminologyJson.NonEmpty(property, "code") is not { } propertyCode || TerminologyJson.ValueX(property) is not { } value)
            {
                continue;
            }

            concept.Properties.Add(new ConceptProperty(propertyCode, value.Value, value.Type));
            concept.Classify(propertyCode, value.Value, roles);
        }

        return concept;
    }

    private void Classify(string propertyCode, string value, PropertyRoles roles)
    {
        if (roles.Parent.Contains(propertyCode))
        {
            ParentCodes.Add(value);
        }
        else if (roles.Child.Contains(propertyCode))
        {
            ChildCodes.Add(value);
        }
        else if (roles.Status.Contains(propertyCode))
        {
            Status = value;
            Inactive |= value is "retired" or "inactive";
        }
        else if (roles.Inactive.Contains(propertyCode))
        {
            Inactive |= value == "true";
        }
    }
}
