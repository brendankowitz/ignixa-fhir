// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using EnsureThat;
using Ignixa.Search.Definition;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Abstractions;

namespace Ignixa.Search.Parsing;

/// <summary>
/// Builds SearchOptions from parsed query parameters using the ExpressionParser.
/// </summary>
public class SearchOptionsBuilder : ISearchOptionsBuilder
{
    private const int DefaultMaxItemCount = 10;
    private const int MaxAllowedItemCount = 1000;

    private readonly IExpressionParser _expressionParser;
    private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchOptionsBuilder"/> class.
    /// </summary>
    /// <param name="expressionParser">The expression parser for building search expressions.</param>
    /// <param name="searchParameterDefinitionManager">The search parameter definition manager for looking up parameter metadata.</param>
    public SearchOptionsBuilder(
        IExpressionParser expressionParser,
        ISearchParameterDefinitionManager searchParameterDefinitionManager)
    {
        EnsureArg.IsNotNull(expressionParser, nameof(expressionParser));
        EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
        _expressionParser = expressionParser;
        _searchParameterDefinitionManager = searchParameterDefinitionManager;
    }

    /// <summary>
    /// Builds SearchOptions from parsed query parameters.
    /// </summary>
    /// <param name="resourceType">The resource type being searched (e.g., "Patient"), or null for system-wide search.</param>
    /// <param name="parameters">The parsed query parameters.</param>
    /// <param name="schemaProvider">Optional schema provider for validating _elements parameter.</param>
    /// <returns>A SearchOptions instance configured according to the parameters.</returns>
    public SearchOptions Build(
        string? resourceType,
        IReadOnlyList<QueryParameter> parameters,
        ISchema? schemaProvider = null)
    {
        EnsureArg.IsNotNull(parameters, nameof(parameters));

        var options = new SearchOptions
        {
            ResourceType = resourceType,
            MaxItemCount = DefaultMaxItemCount,
        };

        // STEP 1: Categorize parameters
        var searchExpressions = new List<Expression>();
        var sortParameters = new List<string>();
        var includeParameters = new List<string>();
        var revIncludeParameters = new List<string>();
        var elementsParameters = new List<string>();
        var unsupportedParameters = new List<string>();

        // For system-wide search (resourceType is null), use "Resource" as base type
        // This allows searching with common parameters like _tag, _profile, _security, _id, _lastUpdated
        // which are defined on the base Resource type and inherited by all resource types.
        string[] resourceTypes = resourceType != null ? new[] { resourceType } : new[] { "Resource" };

        foreach (var param in parameters)
        {
            try
            {
                switch (param.Category)
                {
                    case ParameterCategory.ContinuationToken:
                        options.ContinuationToken = param.Value;
                        break;

                    case ParameterCategory.Count:
                        if (!int.TryParse(param.Value, out int count))
                        {
                            throw new BadSearchRequestException(
                                $"The '_count' parameter value '{param.Value}' is not a valid integer.");
                        }

                        if (count < 0)
                        {
                            throw new BadSearchRequestException(
                                $"The '_count' parameter value must be a non-negative integer.");
                        }

                        options.MaxItemCount = Math.Min(Math.Max(1, count), MaxAllowedItemCount);
                        break;

                    case ParameterCategory.Total:
                        options.Total = ParseTotalType(param.Value);
                        break;

                    case ParameterCategory.Summary:
                        options.Summary = ParseSummaryType(param.Value);
                        // Per FHIR spec, _summary=count implies the total count should be returned
                        // Auto-set Total to Accurate when Summary=count (unless explicitly overridden)
                        if (options.Summary == SummaryType.Count && options.Total == TotalType.None)
                        {
                            options.Total = TotalType.Accurate;
                        }
                        break;

                    case ParameterCategory.Sort:
                        sortParameters.Add(param.Value);
                        break;

                    case ParameterCategory.Include:
                        includeParameters.Add(param.Value);
                        break;

                    case ParameterCategory.RevInclude:
                        revIncludeParameters.Add(param.Value);
                        break;

                    case ParameterCategory.Elements:
                        elementsParameters.Add(param.Value);
                        break;

                    case ParameterCategory.Search:
                        // Use ExpressionParser to parse the search parameter
                        Expression expr = _expressionParser.Parse(resourceTypes, param.Name, param.Value);
                        searchExpressions.Add(expr);
                        break;

                    case ParameterCategory.Control:
                        // Unknown control parameter (starts with _ but not recognized)
                        unsupportedParameters.Add(param.Name);
                        break;
                }
            }
            catch (SearchParameterNotSupportedException)
            {
                unsupportedParameters.Add(param.Name);
            }
            catch (InvalidSearchOperationException)
            {
                unsupportedParameters.Add(param.Name);
            }
        }

        // STEP 2: Combine search expressions with AND
        if (searchExpressions.Count == 1)
        {
            options.Expression = searchExpressions[0]
                .AcceptVisitor(DateTimeEqualityRewriter.Instance, null);
        }
        else if (searchExpressions.Count > 1)
        {
            options.Expression = Expression.And(searchExpressions.ToArray())
                .AcceptVisitor(DateTimeEqualityRewriter.Instance, null);
        }

        // STEP 3: Parse sorting
        if (sortParameters.Count > 0)
        {
            options.Sort = ParseSortParameters(resourceTypes, sortParameters, unsupportedParameters);
        }

        // STEP 4: Parse includes
        if (includeParameters.Count > 0)
        {
            options.Include = ParseIncludeParameters(resourceTypes, includeParameters, isReversed: false);
        }

        // STEP 5: Parse reverse includes
        if (revIncludeParameters.Count > 0)
        {
            options.RevInclude = ParseIncludeParameters(resourceTypes, revIncludeParameters, isReversed: true);
        }

        // STEP 6: Parse and validate elements
        var bundleIssues = new List<IssueComponent>();
        if (elementsParameters.Count > 0)
        {
            // Validate that _elements is not empty (FHIR spec: empty _elements is invalid)
            bool hasEmptyElements = elementsParameters.Any(p => string.IsNullOrWhiteSpace(p));
            if (hasEmptyElements)
            {
                throw new BadSearchRequestException(
                    "The '_elements' parameter value cannot be empty.");
            }

            var parsedElements = ParseElementsParameters(elementsParameters);

            // Validate that _elements resulted in at least one valid element name
            if (parsedElements.Count == 0)
            {
                throw new BadSearchRequestException(
                    "The '_elements' parameter value cannot be empty.");
            }

            // Validate _elements + _summary conflict
            // FHIR spec: _elements can only be used with _summary=false or without _summary
            if (options.Summary != SummaryType.None && options.Summary != SummaryType.False)
            {
                throw new BadSearchRequestException(Resources.ElementsAndSummaryParametersAreIncompatible);
            }

            // Only validate elements if we have a specific resource type
            // For system-wide search (null resourceType), we can't validate elements against a schema
            if (resourceType != null)
            {
                var (validElements, invalidElements) = ValidateElementsAgainstSchema(
                    resourceType,
                    parsedElements,
                    schemaProvider);

                options.Elements = validElements;

                // Add bundle issues for invalid elements
                foreach (var invalidElement in invalidElements)
                {
                    bundleIssues.Add(new IssueComponent(
                        Severity: "warning",
                        Code: "not-found",
                        Diagnostics: $"Element '{invalidElement}' is not a valid property for resource type '{resourceType}'"));
                }
            }
            else
            {
                // For system-wide search, accept all elements without validation
                options.Elements = parsedElements;
            }
        }

        // STEP 7: Record unsupported parameters and create Bundle issues
        options.UnsupportedParams = unsupportedParameters;
        foreach (var param in unsupportedParameters)
        {
            var diagnostics = $"Search parameter '{param}' is not supported";

            bundleIssues.Add(new IssueComponent(
                Severity: "warning",
                Code: "not-supported",
                Diagnostics: diagnostics));
        }
        options.BundleIssues = bundleIssues;

        return options;
    }

    private static TotalType ParseTotalType(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "NONE" => TotalType.None,
            "ACCURATE" => TotalType.Accurate,
            "ESTIMATE" => throw new ForbiddenSearchException(
                string.Format(Resources.UnsupportedTotalParameter, value, "'accurate', 'none'")),
            _ => throw new BadSearchRequestException(
                string.Format(Resources.InvalidTotalParameter, value, "'accurate', 'none'")),
        };
    }

    private static SummaryType ParseSummaryType(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "TRUE" => SummaryType.True,
            "FALSE" => SummaryType.False,
            "TEXT" => SummaryType.Text,
            "DATA" => SummaryType.Data,
            "COUNT" => SummaryType.Count,
            _ => throw new InvalidSearchOperationException(
                $"Invalid _summary parameter value: '{value}'. Valid values are: true, false, text, data, count"),
        };
    }

    private IReadOnlyList<SortExpression> ParseSortParameters(
        string[] resourceTypes,
        List<string> sortParameters,
        List<string> unsupportedParameters)
    {
        var sortExpressions = new List<SortExpression>();

        foreach (string sortParam in sortParameters)
        {
            // Sort format: "field1,-field2" (prefix '-' means descending)
            string[] fields = sortParam.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (string field in fields)
            {
                string trimmedField = field.Trim();

                if (string.IsNullOrEmpty(trimmedField))
                {
                    continue;
                }

                // Parse direction: '-' prefix means descending
                bool isDescending = trimmedField.StartsWith('-');
                string fieldName = isDescending ? trimmedField.Substring(1) : trimmedField;
                var sortOrder = isDescending ? Expressions.SortOrder.Descending : Expressions.SortOrder.Ascending;

                try
                {
                    // Look up the search parameter directly (no need to parse a value for sorting)
                    // For sorting, we only need the SearchParameterInfo metadata, not a parsed value expression
                    if (!_searchParameterDefinitionManager.TryGetSearchParameter(resourceTypes[0], fieldName, out SearchParameterInfo searchParameter))
                    {
                        // Field is not a valid search parameter - record the unsupported field
                        System.Diagnostics.Debug.WriteLine($"Sort field '{fieldName}' is not supported for resource type: {resourceTypes[0]}");
                        unsupportedParameters.Add($"_sort={fieldName}");
                        continue;
                    }

                    sortExpressions.Add(new SortExpression(searchParameter, sortOrder));
                    System.Diagnostics.Debug.WriteLine($"✅ Added sort expression: {searchParameter.Code} ({searchParameter.Type}) {sortOrder}");
                }
                catch (Exception ex)
                {
                    // Log other exceptions for debugging, add to unsupported, then skip
                    System.Diagnostics.Debug.WriteLine($"Failed to parse sort field '{fieldName}': {ex.GetType().Name} - {ex.Message}");
                    unsupportedParameters.Add($"_sort={fieldName}");
                    continue;
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"ParseSortParameters returning {sortExpressions.Count} sort expression(s)");
        return sortExpressions;
    }

    private IReadOnlyList<IncludeExpression> ParseIncludeParameters(string[] resourceTypes, List<string> includeParameters, bool isReversed)
    {
        var includeExpressions = new List<IncludeExpression>();

        foreach (string includeParam in includeParameters)
        {
            try
            {
                // Use ExpressionParser to parse include expressions
                IncludeExpression includeExpr = _expressionParser.ParseInclude(resourceTypes, includeParam, isReversed, iterate: false);
                includeExpressions.Add(includeExpr);
            }
            catch
            {
                // Skip invalid include parameters
                continue;
            }
        }

        return includeExpressions;
    }

    private static IReadOnlySet<string> ParseElementsParameters(List<string> elementsParameters)
    {
        var elements = new HashSet<string>(StringComparer.Ordinal);

        // Elements format: "element1,element2,element3"
        var trimmedElements = elementsParameters
            .SelectMany(param => param.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(elementName => elementName.Trim())
            .Where(trimmedElement => !string.IsNullOrEmpty(trimmedElement));

        elements.UnionWith(trimmedElements);
        return elements;
    }

    /// <summary>
    /// Validates the requested elements against the schema for the resource type.
    /// Returns a tuple of (validElements, invalidElements).
    /// If schemaProvider is null, all elements are considered valid.
    /// Handles FHIR shorthand normalization (_id → id).
    /// </summary>
    private static (IReadOnlySet<string> validElements, IReadOnlySet<string> invalidElements)
        ValidateElementsAgainstSchema(
            string resourceType,
            IReadOnlySet<string> requestedElements,
            ISchema? schemaProvider)
    {
        var validElements = new HashSet<string>(StringComparer.Ordinal);
        var invalidElements = new HashSet<string>(StringComparer.Ordinal);

        // If no schema provider, accept all elements
        if (schemaProvider == null)
        {
            return (validElements: requestedElements, invalidElements: new HashSet<string>());
        }

        // Get schema for this resource type
        var schema = schemaProvider.GetTypeDefinition(resourceType);
        if (schema == null)
        {
            // If schema not available, accept all elements
            return (validElements: requestedElements, invalidElements: new HashSet<string>());
        }

        // Build set of valid element names from schema
        var schemaElementNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in schema.Children)
        {
            schemaElementNames.Add(element.Info.Name);
        }

        // Validate each requested element
        foreach (var element in requestedElements)
        {
            // Check if element exists in schema directly
            if (schemaElementNames.Contains(element))
            {
                validElements.Add(element);
            }
            // Check if it's the _id shorthand and normalize to id
            else if (element == "_id" && schemaElementNames.Contains("id"))
            {
                validElements.Add("id");
            }
            else
            {
                // Element is not valid for this resource type
                invalidElements.Add(element);
            }
        }

        return (validElements, invalidElements);
    }
}
