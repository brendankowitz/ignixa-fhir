// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>Which property codes carry hierarchy, status and inactivity, by declaration or by conventional name.</summary>
internal sealed record PropertyRoles(HashSet<string> Parent, HashSet<string> Child, HashSet<string> Status, HashSet<string> Inactive)
{
    private const string PropertiesUri = "http://hl7.org/fhir/concept-properties#";

    public static PropertyRoles Of(JsonElement resource)
    {
        var roles = new PropertyRoles(
            new HashSet<string>(["parent", "subsumedBy"], StringComparer.Ordinal),
            new HashSet<string>(["child"], StringComparer.Ordinal),
            new HashSet<string>(["status"], StringComparer.Ordinal),
            new HashSet<string>(["inactive"], StringComparer.Ordinal));
        foreach (JsonElement declared in TerminologyJson.Array(resource, "property"))
        {
            if (TerminologyJson.NonEmpty(declared, "code") is not { } code || TerminologyJson.String(declared, "uri") is not { } uri || !uri.StartsWith(PropertiesUri, StringComparison.Ordinal))
            {
                continue;
            }

            HashSet<string>? role = uri[PropertiesUri.Length..] switch
            {
                "parent" => roles.Parent,
                "child" => roles.Child,
                "status" => roles.Status,
                "inactive" => roles.Inactive,
                _ => null,
            };
            role?.Add(code);
        }

        return roles;
    }
}
