// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using ModelContextProtocol.Server;

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>What the server tells a client about itself when it connects.</summary>
public static class McpServerSetup
{
    /// <summary>
    /// Sent to the client in the initialize result. It is fixed text that points at the cheap paths, so it never holds
    /// anything read from the folder.
    /// </summary>
    public const string Instructions = """
        Read-only FHIR data in one folder. Cheapest paths:
        - Patient: with an id or MRN, search directly with compartment=Patient/<id>; no lookup first. To find one, search Patient with identifier=<value> or name=<text>, or call patient_list with query (name, MRN, id or birth date). Do not list all patients.
        - Scope every other search to the patient with compartment=Patient/<id>.
        - Ask for values, not resources: columns={"date":"authoredOn","drug":"(medication.ofType(CodeableConcept) | medication.ofType(Reference).resolve().code).coding.first().display","prescriber":"requester.resolve().name.first().family"} returns a small table, whichever medication[x] an order uses. .resolve() follows references, conditional ones included, so _include and elements are rarely needed.
        - One value over all matches: fhirpath, for example entry.count() or entry.resource.value.ofType(Quantity).value.max(). summary=count answers "how many".
        - Every search parameter, _sort and _count included, goes inside query. Latest value: query=...&_sort=-date (or the type's date parameter) with count=1. Limit a range with date=ge<yyyy-mm-dd>.
        - Responses are held to about 12 KB. When one is cut, 'next' has the exact call for the rest and ways to narrow it; narrow rather than raise maxBytes.
        - Check 'issues' every time: parameter-ignored means a filter did not apply; column-empty means a path matched nothing.
        """;

    /// <summary>Names the server and gives it its <see cref="Instructions"/>.</summary>
    public static void Configure(McpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ServerInfo = new() { Name = "cli-on-fhir", Version = typeof(McpServerSetup).Assembly.GetName().Version?.ToString() ?? "0.0.0" };
        options.ServerInstructions = Instructions;
    }
}
