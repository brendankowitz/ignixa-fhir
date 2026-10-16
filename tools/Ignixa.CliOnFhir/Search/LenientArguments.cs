// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// The argument combinations of a search whose intent is unambiguous, fixed with an <see cref="Code"/> warning instead of
/// failing the call (ADR 2610). About 60 of 87 failed searches in the fhir-question-bench runs were these, each costing the
/// agent a turn to send the call it meant. The list is closed: any other conflicting combination still fails.
/// </summary>
internal static class LenientArguments
{
    /// <summary>The warning code of every adjustment.</summary>
    public const string Code = "argument-adjusted";

    private static readonly IReadOnlySet<string> ElementsParameter = new HashSet<string>(["_elements"], StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> CountParameter = new HashSet<string>(["_count"], StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> SortParameter = new HashSet<string>(["_sort"], StringComparer.Ordinal);

    /// <summary>The request with each listed conflict resolved, and one warning per adjustment, in the order applied.</summary>
    public static (SearchRequest Request, IReadOnlyList<IssueItem> Warnings) Apply(SearchRequest request)
    {
        List<IssueItem> warnings = [];
        request = KeepColumnsOverFhirPath(request, warnings);
        request = IgnoreElementsBesideColumns(request, warnings);
        request = RenameKeyColumn(request, warnings);
        request = IgnorePagingBesideFhirPath(request, warnings);
        request = DropSortBesideDatePick(request, warnings);
        return (request, warnings);
    }

    private static SearchRequest KeepColumnsOverFhirPath(SearchRequest request, List<IssueItem> warnings)
    {
        if (request.Columns is null || request.FhirPath is null)
        {
            return request;
        }

        warnings.Add(Adjusted("'columns' and 'fhirpath' were both given: the columns were returned and 'fhirpath' ignored. Send one of them."));
        return request with { FhirPath = null };
    }

    private static SearchRequest IgnoreElementsBesideColumns(SearchRequest request, List<IssueItem> warnings)
    {
        if (request.Columns is null)
        {
            return request;
        }

        if (request.Elements is not null)
        {
            warnings.Add(Adjusted("'elements' was ignored: 'columns' already choose what each row holds."));
            request = request with { Elements = null };
        }

        if (SearchNarrowing.ParameterNames(request.Query).Contains("_elements"))
        {
            warnings.Add(Adjusted("_elements was dropped from the query: 'columns' already choose what each row holds."));
            request = request with { Query = SearchNarrowing.Without(request.Query, ElementsParameter) };
        }

        return request;
    }

    private static SearchRequest RenameKeyColumn(SearchRequest request, List<IssueItem> warnings)
    {
        if (request.Columns is not { } columns || !columns.Any(column => column.Name == ColumnSearch.KeyColumn))
        {
            return request;
        }

        HashSet<string> taken = [.. columns.Select(column => column.Name)];
        string renamed = Enumerable.Range(1, columns.Count + 1).Select(index => $"{ColumnSearch.KeyColumn}_{index}").First(name => !taken.Contains(name));
        warnings.Add(Adjusted($"The column '{ColumnSearch.KeyColumn}' was renamed '{renamed}': '{ColumnSearch.KeyColumn}' is the key column every row starts with (the row's resource)."));
        return request with { Columns = [.. columns.Select(column => column.Name == ColumnSearch.KeyColumn ? column with { Name = renamed } : column)] };
    }

    private static SearchRequest IgnorePagingBesideFhirPath(SearchRequest request, List<IssueItem> warnings)
    {
        bool countInQuery = SearchNarrowing.ParameterNames(request.Query).Contains("_count");
        if (request.FhirPath is null || (request.Count is null && request.Offset == 0 && !countInQuery))
        {
            return request;
        }

        warnings.Add(Adjusted("'count', '_count' and 'offset' were ignored: 'fhirpath' is evaluated once over every match."));
        return request with { Count = null, Offset = 0, Query = countInQuery ? SearchNarrowing.Without(request.Query, CountParameter) : request.Query };
    }

    private static SearchRequest DropSortBesideDatePick(SearchRequest request, List<IssueItem> warnings)
    {
        if (request.PerPatient is not (PerPatient.Latest or PerPatient.Earliest) || !SearchNarrowing.ParameterNames(request.Query).Contains("_sort"))
        {
            return request;
        }

        warnings.Add(Adjusted($"_sort was dropped from the query: perPatient={request.PerPatient} orders the matches by date itself. For an order of your own, use perPatient=first with that _sort."));
        return request with { Query = SearchNarrowing.Without(request.Query, SortParameter) };
    }

    private static IssueItem Adjusted(string diagnostics) => new("warning", Code, diagnostics, Location: null);
}
