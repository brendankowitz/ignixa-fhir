// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using EnsureThat;
using Ignixa.Domain.Constants;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.SourceNodeSerialization.Abstractions;

namespace Ignixa.Search.Parsing;

/// <summary>
/// Builds SearchOptions from parsed query parameters using the ExpressionParser.
/// </summary>
public class SearchOptionsBuilder : ISearchOptionsBuilder
{
    private const int DefaultMaxItemCount = 10;
    private const int MaxAllowedItemCount = 1000;

    private readonly IExpressionParser _expressionParser;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchOptionsBuilder"/> class.
    /// </summary>
    /// <param name="expressionParser">The expression parser for building search expressions.</param>
    public SearchOptionsBuilder(IExpressionParser expressionParser)
    {
        EnsureArg.IsNotNull(expressionParser, nameof(expressionParser));
        _expressionParser = expressionParser;
    }

    /// <summary>
    /// Builds SearchOptions from parsed query parameters.
    /// </summary>
    /// <param name="resourceType">The resource type being searched (e.g., "Patient").</param>
    /// <param name="parameters">The parsed query parameters.</param>
    /// <param name="schemaProvider">Optional schema provider for validating _elements parameter.</param>
    /// <returns>A SearchOptions instance configured according to the parameters.</returns>
    public SearchOptions Build(
        string resourceType,
        IReadOnlyList<QueryParameter> parameters,
        IStructureDefinitionSummaryProvider? schemaProvider = null)
    {
        EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));
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

        string[] resourceTypes = new[] { resourceType };

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
                        if (int.TryParse(param.Value, out int count))
                        {
                            options.MaxItemCount = Math.Min(Math.Max(1, count), MaxAllowedItemCount);
                        }
                        break;

                    case ParameterCategory.Total:
                        options.Total = ParseTotalType(param.Value);
                        break;

                    case ParameterCategory.Summary:
                        options.Summary = ParseSummaryType(param.Value);
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
            options.Expression = searchExpressions[0];
        }
        else if (searchExpressions.Count > 1)
        {
            options.Expression = Expression.And(searchExpressions.ToArray());
        }

        // STEP 3: Parse sorting
        if (sortParameters.Count > 0)
        {
            options.Sort = ParseSortParameters(resourceTypes, sortParameters);
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
            var parsedElements = ParseElementsParameters(elementsParameters);
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
            "ESTIMATE" => TotalType.Estimate,
            _ => TotalType.None,
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

    private IReadOnlyList<SortExpression> ParseSortParameters(string[] resourceTypes, List<string> sortParameters)
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
                    // Parse the field as a search parameter to get the SearchParameterInfo
                    Expression fieldExpression = _expressionParser.Parse(resourceTypes, fieldName, "dummy");

                    if (fieldExpression is SearchParameterExpression searchParamExpr)
                    {
                        sortExpressions.Add(new SortExpression(searchParamExpr.Parameter, sortOrder));
                    }
                }
                catch
                {
                    // Skip invalid sort parameters
                    continue;
                }
            }
        }

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
            IStructureDefinitionSummaryProvider? schemaProvider)
    {
        var validElements = new HashSet<string>(StringComparer.Ordinal);
        var invalidElements = new HashSet<string>(StringComparer.Ordinal);

        // If no schema provider, accept all elements
        if (schemaProvider == null)
        {
            return (validElements: requestedElements, invalidElements: new HashSet<string>());
        }

        // Get schema for this resource type
        var schema = schemaProvider.Provide(resourceType);
        if (schema == null)
        {
            // If schema not available, accept all elements
            return (validElements: requestedElements, invalidElements: new HashSet<string>());
        }

        // Build set of valid element names from schema
        var schemaElementNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in schema.GetElements())
        {
            schemaElementNames.Add(element.ElementName);
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
