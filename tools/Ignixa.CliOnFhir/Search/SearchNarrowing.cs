// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// The calls <c>next</c> offers when a search page was cut: the exact call for the rest, then ways to ask for less. They are
/// built from the request and the resource type's name only.
/// </summary>
internal static class SearchNarrowing
{
    public const string Tool = "search_fhir_resources";

    /// <summary>Fewer remaining matches than this are not worth a count-only suggestion.</summary>
    private const int CountWorthwhile = ResourceSearcher.DefaultCount;

    /// <summary>Top-level elements that answer most questions about a type: its code, value and date.</summary>
    private static readonly Dictionary<string, string> Elements = new(StringComparer.Ordinal)
    {
        ["Observation"] = "code,value[x],effective[x]",
        ["Condition"] = "code,clinicalStatus,onset[x]",
        ["MedicationRequest"] = "medication[x],authoredOn,status",
        ["Encounter"] = "type,class,period",
        ["Procedure"] = "code,performed[x],status",
        ["DiagnosticReport"] = "code,effective[x],status",
        ["Immunization"] = "vaccineCode,occurrence[x],status",
        ["AllergyIntolerance"] = "code,clinicalStatus",
        ["Patient"] = "name,birthDate,gender,identifier",
    };

    /// <summary>The search parameter that limits a type to a date range.</summary>
    private static readonly Dictionary<string, string> DateParameters = new(StringComparer.Ordinal)
    {
        ["Observation"] = "date",
        ["Encounter"] = "date",
        ["Procedure"] = "date",
        ["DiagnosticReport"] = "date",
        ["Immunization"] = "date",
        ["MedicationRequest"] = "authoredon",
        ["Condition"] = "recorded-date",
    };

    /// <summary>The search as asked, starting at <paramref name="offset"/>.</summary>
    public static NextCall Call(SearchRequest request, int offset)
    {
        Dictionary<string, object> args = new(StringComparer.Ordinal) { ["resourceType"] = request.ResourceType };
        if (!string.IsNullOrEmpty(request.Query))
        {
            args["query"] = request.Query;
        }

        if (request.Compartment is not null)
        {
            args["compartment"] = request.Compartment;
        }

        if (request.Count is int count)
        {
            args["count"] = count;
        }

        if (request.Elements is not null)
        {
            args["elements"] = request.Elements;
        }

        if (request.Lenient)
        {
            args["lenient"] = true;
        }

        if (request.MaxBytes is int maxBytes)
        {
            args["maxBytes"] = maxBytes;
        }

        if (offset > 0)
        {
            args["offset"] = offset;
        }

        return new NextCall(Tool, args);
    }

    /// <summary>
    /// The calls that follow a page cut at <paramref name="resume"/>: that rest of the matches first, then the same page with
    /// <c>elements</c>, a date range, only the total, or the largest budget, each only where it could help. A page the caller
    /// asked to be small, or that left only a few matches, gets just the call for the rest.
    /// </summary>
    /// <param name="cutBySize">Whether the response budget, rather than the page size, ended the page.</param>
    public static List<NextCall> After(SearchRequest request, int total, int resume, bool cutBySize, int budget)
    {
        NextCall current = Call(request, request.Offset);
        List<NextCall> calls = [Call(request, resume)];
        bool askedForFew = resume - request.Offset < CountWorthwhile;
        if (!cutBySize && (askedForFew || total - resume < CountWorthwhile))
        {
            return calls;
        }

        if (request.Elements is null)
        {
            calls.Add(TruncationCalls.Trim(current, Elements.GetValueOrDefault(request.ResourceType)));
        }

        if (DateParameters.TryGetValue(request.ResourceType, out string? parameter) && request.Query?.Contains(parameter + "=", StringComparison.Ordinal) != true)
        {
            calls.Add(TruncationCalls.With(
                current,
                $"this page again with {parameter}=geYYYY-MM-DD added to the query to limit the date range; _sort=-{parameter} with count=1 gives the latest",
                offset: null,
                elements: null,
                maxBytes: null));
        }

        if (total - request.Offset > CountWorthwhile)
        {
            calls.Add(Count(request));
        }

        if (cutBySize && budget < EnvelopeJson.MaxBytes)
        {
            calls.Add(TruncationCalls.Larger(current));
        }

        return calls;
    }

    private static NextCall Count(SearchRequest request)
    {
        Dictionary<string, object> args = new(StringComparer.Ordinal) { ["resourceType"] = request.ResourceType, ["summary"] = "count" };
        if (!string.IsNullOrEmpty(request.Query))
        {
            args["query"] = request.Query;
        }

        if (request.Compartment is not null)
        {
            args["compartment"] = request.Compartment;
        }

        return new NextCall(Tool, args, "only the number of matches, no resources");
    }
}
