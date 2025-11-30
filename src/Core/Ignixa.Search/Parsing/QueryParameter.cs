// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Parsing;

/// <summary>
/// Represents a single query string parameter from the HTTP request.
/// </summary>
/// <param name="Name">The parameter name (e.g., "name", "_count", "ct").</param>
/// <param name="Value">The parameter value.</param>
public record QueryParameter(string Name, string Value)
{
    /// <summary>
    /// Gets the category of this parameter based on its name.
    /// </summary>
    public ParameterCategory Category => ClassifyParameter(Name);

    private static ParameterCategory ClassifyParameter(string name)
    {
        return name switch
        {
            // Continuation token for paging (ct = legacy, after = new standard)
            "ct" or "after" => ParameterCategory.ContinuationToken,

            // Control parameters (pagination, sorting, result formatting)
            "_count" => ParameterCategory.Count,
            "_total" => ParameterCategory.Total,
            "_summary" => ParameterCategory.Summary,
            "_sort" => ParameterCategory.Sort,
            "_include" => ParameterCategory.Include,
            "_revinclude" => ParameterCategory.RevInclude,
            "_elements" => ParameterCategory.Elements,
            "_type" => ParameterCategory.Type,
            "_format" or "_pretty" => ParameterCategory.Formatting,
            "_contained" or "_containedType" => ParameterCategory.Control,

            // Search parameters (filter resources)
            "_id" => ParameterCategory.Search,
            "_tag" => ParameterCategory.Search,
            "_lastUpdated" => ParameterCategory.Search,
            "_filter" => ParameterCategory.Search,
            "_list" => ParameterCategory.Search,
            _ when name.StartsWith("_has", StringComparison.Ordinal) => ParameterCategory.Search,

            // Other underscore parameters are control parameters
            _ when name.StartsWith('_') => ParameterCategory.Control,

            // Everything else is a search parameter
            _ => ParameterCategory.Search,
        };
    }
}

/// <summary>
/// Categorizes query parameters for processing.
/// </summary>
public enum ParameterCategory
{
    /// <summary>
    /// Search parameter (e.g., "name=John", "birthdate=gt2000-01-01", "_tag", "_filter", "_list").
    /// </summary>
    Search,

    /// <summary>
    /// Continuation token for paging (ct or after).
    /// </summary>
    ContinuationToken,

    /// <summary>
    /// Count parameter (_count) - number of results per page.
    /// </summary>
    Count,

    /// <summary>
    /// Total parameter (_total) - include total count in results.
    /// </summary>
    Total,

    /// <summary>
    /// Summary parameter (_summary) - return only summary elements.
    /// </summary>
    Summary,

    /// <summary>
    /// Sort parameter (_sort) - order results.
    /// </summary>
    Sort,

    /// <summary>
    /// Include parameter (_include) - include related resources.
    /// </summary>
    Include,

    /// <summary>
    /// Reverse include parameter (_revinclude) - include resources that reference matches.
    /// </summary>
    RevInclude,

    /// <summary>
    /// Elements parameter (_elements) - filters which elements to return.
    /// </summary>
    Elements,

    /// <summary>
    /// Resource type parameter (_type) - filter by resource type in multi-resource searches.
    /// </summary>
    Type,

    /// <summary>
    /// Formatting parameter (_format, _pretty) - response format control.
    /// </summary>
    Formatting,

    /// <summary>
    /// Other control parameters (start with underscore but not recognized).
    /// </summary>
    Control,
}
