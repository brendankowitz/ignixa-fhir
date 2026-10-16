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

    /// <summary>Columns that answer most list questions about a type, as a table instead of resources.</summary>
    private static readonly Dictionary<string, IReadOnlyList<SearchColumn>> Columns = new(StringComparer.Ordinal)
    {
        ["Observation"] = [new("date", "effective.ofType(dateTime)"), new("code", "code.coding.first().code"), new("display", "code.coding.first().display"), new("value", "value.ofType(Quantity).value"), new("unit", "value.ofType(Quantity).unit")],
        ["Condition"] = [new("onset", "onset.ofType(dateTime)"), new("code", "code.coding.first().code"), new("display", "code.coding.first().display"), new("clinicalStatus", "clinicalStatus.coding.first().code")],
        ["MedicationRequest"] = [new("date", "authoredOn"), new("drug", "(medication.ofType(CodeableConcept) | medication.ofType(Reference).resolve().code).coding.first().display"), new("status", "status")],
        ["Encounter"] = [new("start", "period.start"), new("class", "class.code"), new("type", "type.first().coding.first().display")],
        ["Procedure"] = [new("start", "performed.ofType(Period).start"), new("code", "code.coding.first().code"), new("display", "code.coding.first().display")],
        ["DiagnosticReport"] = [new("date", "effective.ofType(dateTime)"), new("code", "code.coding.first().code"), new("display", "code.coding.first().display"), new("results", "result.count()")],
        ["Immunization"] = [new("date", "occurrence.ofType(dateTime)"), new("vaccine", "vaccineCode.coding.first().display")],
    };

    /// <summary>The search parameter that limits a type to a date range, and that <c>perPatient</c> orders it by.</summary>
    public static IReadOnlyDictionary<string, string> DateParameters { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
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

        if (request.Columns is not null)
        {
            args["columns"] = AsArgument(request.Columns);
        }

        if (request.PerPatient is not null)
        {
            args["perPatient"] = request.PerPatient;
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
    /// The calls that follow a page cut at <paramref name="resume"/>. First those that answer over every match in one response
    /// instead of more pages (<c>fhirpath</c>, <c>perPatient</c>, <c>summary=count</c>), then the rest of the matches, then the
    /// same page with <c>elements</c>, as a table, with a date range or with the largest budget, each only where it could help.
    /// A page the caller asked to be small, or that left only a few matches, gets just the call for the rest.
    /// </summary>
    /// <param name="cutBySize">Whether the response budget, rather than the page size, ended the page.</param>
    /// <param name="largerBudget">The largest budget the server allows, when it is more than this call had; null otherwise.</param>
    public static List<NextCall> After(SearchRequest request, int total, int resume, bool cutBySize, int? largerBudget)
    {
        NextCall current = Call(request, request.Offset);
        NextCall rest = Call(request, resume);
        bool askedForFew = resume - request.Offset < CountWorthwhile;
        if (!cutBySize && (askedForFew || total - resume < CountWorthwhile))
        {
            return [rest];
        }

        List<NextCall> calls = [.. WholeAnswers(request, total), rest];
        if (request.Columns is null && request.Elements is null)
        {
            calls.Add(TruncationCalls.Trim(current, Elements.GetValueOrDefault(request.ResourceType)));
        }

        if (request.Columns is null && Columns.TryGetValue(request.ResourceType, out IReadOnlyList<SearchColumn>? columns))
        {
            Dictionary<string, object> args = new(current.Args, StringComparer.Ordinal) { ["columns"] = AsArgument(columns) };
            args.Remove("elements");
            calls.Add(new NextCall(
                Tool,
                args,
                "this page again as a table of values instead of resources, so many more fit; change the columns to the FHIRPath you need (.resolve() follows a reference)"));
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

        if (cutBySize && largerBudget is int larger)
        {
            calls.Add(TruncationCalls.Larger(current, larger));
        }

        return calls;
    }

    /// <summary>
    /// The calls that answer over every match in one response rather than page by page: one FHIRPath value, one match per
    /// patient (outside a single patient's compartment), and the count.
    /// </summary>
    private static IEnumerable<NextCall> WholeAnswers(SearchRequest request, int total)
    {
        if (total <= ExpressionSearch.MaxMatches)
        {
            Dictionary<string, object> args = Whole(request);
            args["fhirpath"] = "entry.count()";
            yield return new NextCall(
                Tool,
                args,
                $"all {total} matches in one value instead of pages: set fhirpath to what the question needs, e.g. entry.resource.where(value.ofType(Quantity).value >= 130).count()");
        }

        if (request.PerPatient is null && request.Compartment is null && DateParameters.TryGetValue(request.ResourceType, out string? date))
        {
            bool sorted = request.Query?.Contains("_sort=", StringComparison.Ordinal) == true;
            NextCall perPatient = Call(request with { PerPatient = sorted ? PerPatient.First : PerPatient.Latest }, offset: 0);
            yield return perPatient with
            {
                Hint = sorted
                    ? "one match per patient, the first in this _sort order; add fhirpath or summary=count for a cohort answer"
                    : $"one match per patient, the latest by {date}; add fhirpath or summary=count for a cohort answer",
            };
        }

        if (total - request.Offset > CountWorthwhile)
        {
            yield return Count(request);
        }
    }
    /// <summary>Columns as the tool takes them: an object mapping each name to its FHIRPath, in column order.</summary>
    private static Dictionary<string, string> AsArgument(IReadOnlyList<SearchColumn> columns)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (SearchColumn column in columns)
        {
            map[column.Name] = column.Path;
        }

        return map;
    }

    private static NextCall Count(SearchRequest request)
    {
        Dictionary<string, object> args = Whole(request);
        args["summary"] = "count";
        return new NextCall(Tool, args, request.PerPatient is null ? "only the number of matches, no resources" : "only the number of patients, no resources");
    }

    /// <summary>The arguments that choose the matches (type, query, compartment, perPatient, lenient), without paging or projection.</summary>
    private static Dictionary<string, object> Whole(SearchRequest request)
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

        if (request.PerPatient is not null)
        {
            args["perPatient"] = request.PerPatient;
        }

        if (request.Lenient)
        {
            args["lenient"] = true;
        }

        return args;
    }
}
