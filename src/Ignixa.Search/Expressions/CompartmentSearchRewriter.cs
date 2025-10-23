// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Rewrites <see cref="CompartmentSearchExpression"/> into search parameter expressions.
/// Implements the FHIR compartment search pattern by expanding compartment membership
/// rules into OR'd search parameter equality expressions.
///
/// Supports wildcard searches (Patient/123/*) by expanding to all resource types
/// in the compartment definition when FilteredResourceTypes is empty.
///
/// Example 1: Single resource type - CompartmentSearchExpression("Patient", "123", {}) for Observation
/// Rewrites to: (subject = Patient/123) OR (performer = Patient/123)
///
/// Example 2: Wildcard - CompartmentSearchExpression("Patient", "123", empty) with context.ResourceType = "*"
/// Returns: Union of expressions for ALL resource types in Patient compartment
/// (Observation: (subject = Patient/123) OR (performer = Patient/123))
/// OR (Condition: (subject = Patient/123) OR (verifier = Patient/123))
/// OR ...
/// </summary>
public class CompartmentSearchRewriter : ExpressionRewriter<(string ResourceType, ICompartmentDefinitionManager CompartmentManager, ISearchParameterDefinitionManager SearchParameterManager)>
{
    public override Expression VisitCompartment(
        CompartmentSearchExpression expression,
        (string ResourceType, ICompartmentDefinitionManager CompartmentManager, ISearchParameterDefinitionManager SearchParameterManager) context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull(context.CompartmentManager, nameof(context.CompartmentManager));
        EnsureArg.IsNotNull(context.SearchParameterManager, nameof(context.SearchParameterManager));

        // Validate compartment ID is not empty
        if (string.IsNullOrWhiteSpace(expression.CompartmentId))
        {
            throw new InvalidSearchOperationException($"Compartment ID is invalid or empty");
        }

        // Convert compartment type string to enum (e.g., "Patient" -> CompartmentType.Patient)
        if (!Enum.TryParse<CompartmentType>(expression.CompartmentType, out var compartmentType))
        {
            throw new InvalidSearchOperationException($"Compartment type '{expression.CompartmentType}' is invalid. Must be one of: Patient, Practitioner, RelatedPerson, Device, Encounter");
        }

        // Check if this is a wildcard search (FilteredResourceTypes is empty or context.ResourceType is "*")
        bool isWildcard = expression.FilteredResourceTypes.Count == 0 || context.ResourceType == "*";

        if (isWildcard)
        {
            // Wildcard search: expand to all resource types in the compartment
            return BuildWildcardExpression(expression, compartmentType, context);
        }
        else
        {
            // Single resource type search
            return BuildSingleResourceTypeExpression(expression, compartmentType, context.ResourceType, context);
        }
    }

    /// <summary>
    /// Builds a Union expression for wildcard compartment searches.
    /// Groups resource types by shared compartment parameters, then uses InExpressions for efficiency.
    /// </summary>
    private Expression BuildWildcardExpression(
        CompartmentSearchExpression expression,
        CompartmentType compartmentType,
        (string ResourceType, ICompartmentDefinitionManager CompartmentManager, ISearchParameterDefinitionManager SearchParameterManager) context)
    {
        // Get all resource types for this compartment
        if (!context.CompartmentManager.TryGetResourceTypes(compartmentType, out var allResourceTypes))
        {
            throw new InvalidSearchOperationException($"No resource types found for compartment type: {expression.CompartmentType}");
        }

        // Determine which resource types to search
        var resourceTypesToSearch = expression.FilteredResourceTypes.Count > 0
            ? allResourceTypes.Where(rt => expression.FilteredResourceTypes.Contains(rt)).ToHashSet()
            : allResourceTypes;

        if (resourceTypesToSearch.Count == 0)
        {
            throw new InvalidSearchOperationException($"No matching resource types found for compartment search");
        }

        // Group by search parameter (to create efficient IN clauses instead of ORs)
        var compartmentSearchExpressionsByParameter = new Dictionary<string, (List<Expression> Expressions, HashSet<string> ResourceTypes)>();

        foreach (var resourceType in resourceTypesToSearch)
        {
            // Get search parameters that define compartment membership for this resource type
            if (!context.CompartmentManager.TryGetSearchParams(resourceType, compartmentType, out var compartmentSearchParams))
            {
                continue;
            }

            foreach (string searchParamCode in compartmentSearchParams)
            {
                SearchParameterInfo searchParam;
                try
                {
                    searchParam = context.SearchParameterManager.GetSearchParameter(resourceType, searchParamCode);
                }
                catch (Exception)
                {
                    continue;
                }

                // Only process reference-type parameters
                if (searchParam.Type != SearchParamType.Reference)
                {
                    continue;
                }

                // Use parameter URL as key for grouping (parameters with same URL are equivalent across resource types)
                string parameterKey = searchParam.Url.ToString();

                if (!compartmentSearchExpressionsByParameter.TryGetValue(parameterKey, out var existingGroup))
                {
                    // First resource type with this parameter - create the SearchParameterExpression
                    var referenceExpression = Expression.SearchParameter(
                        searchParam,
                        Expression.StringEquals(FieldName.ReferenceResourceId, componentIndex: null, value: expression.CompartmentId, ignoreCase: false));

                    existingGroup = (new List<Expression> { referenceExpression }, new HashSet<string> { resourceType });
                    compartmentSearchExpressionsByParameter[parameterKey] = existingGroup;
                }
                else
                {
                    // Subsequent resource types with same parameter - just add the resource type
                    existingGroup.ResourceTypes.Add(resourceType);
                }
            }
        }

        if (compartmentSearchExpressionsByParameter.Count == 0)
        {
            throw new InvalidSearchOperationException("No compartment search parameters found");
        }

        // Build grouped expressions with InExpression for resource types
        var groupedExpressions = new List<Expression>();

        foreach (var parameterGroup in compartmentSearchExpressionsByParameter.Values)
        {
            // Each parameter group has exactly one SearchParameterExpression (shared across resource types with that parameter)
            Expression referenceExpression = parameterGroup.Expressions[0];

            // If we're searching multiple resource types, filter by type to avoid cross-resource-type matches
            if (resourceTypesToSearch.Count > 1)
            {
                // Create _type IN (...) expression for resource types that use this parameter
                var resourceTypeSearchParam = context.SearchParameterManager.GetSearchParameter("Resource", "_type");
                var resourceTypeInExpression = Expression.SearchParameter(
                    resourceTypeSearchParam,
                    Expression.In(FieldName.TokenCode, componentIndex: null, parameterGroup.ResourceTypes.ToList()));

                // Combine: _type IN (...) AND parameter_expression
                var combinedExpression = Expression.And(resourceTypeInExpression, referenceExpression);
                groupedExpressions.Add(combinedExpression);
            }
            else
            {
                // Single resource type - no type filter needed
                groupedExpressions.Add(referenceExpression);
            }
        }

        if (groupedExpressions.Count == 1)
        {
            return groupedExpressions[0];
        }

        // Multiple parameter groups - UNION them with UnionExpression for proper SQL generation
        return Expression.Union(UnionOperator.All, groupedExpressions);
    }

    /// <summary>
    /// Builds a compartment expression for a single resource type.
    /// Creates OR'd search parameter expressions for all compartment membership parameters.
    /// </summary>
    private Expression BuildSingleResourceTypeExpression(
        CompartmentSearchExpression expression,
        CompartmentType compartmentType,
        string resourceType,
        (string ResourceType, ICompartmentDefinitionManager CompartmentManager, ISearchParameterDefinitionManager SearchParameterManager) context)
    {
        // Get search parameters that define compartment membership for this resource type
        if (!context.CompartmentManager.TryGetSearchParams(resourceType, compartmentType, out var compartmentSearchParams))
        {
            // Resource type is not in this compartment - return expression that matches nothing
            // Use a non-existent ID to ensure no results match
            return Expression.SearchParameter(
                context.SearchParameterManager.GetSearchParameter(resourceType, "_id"),
                Expression.Equals(FieldName.TokenCode, componentIndex: null, value: "compartment-no-match-impossible-id"));
        }

        // Create search parameter expressions for each compartment membership parameter
        var parameterExpressions = new List<Expression>();

        foreach (string searchParamCode in compartmentSearchParams)
        {
            SearchParameterInfo searchParam;
            try
            {
                searchParam = context.SearchParameterManager.GetSearchParameter(resourceType, searchParamCode);
            }
            catch (Exception)
            {
                // Search parameter not found for this resource type - skip it
                continue;
            }

            // Only process reference-type parameters (compartment membership is always via references)
            if (searchParam.Type != SearchParamType.Reference)
            {
                continue;
            }

            // Create reference equality expression: param = "Patient/123"
            // Match by resourceId only - the compartment definition already constrains the resource type
            var resourceIdExpression = Expression.StringEquals(FieldName.ReferenceResourceId, componentIndex: null, value: expression.CompartmentId, ignoreCase: false);

            // Wrap in SearchParameterExpression
            parameterExpressions.Add(
                Expression.SearchParameter(searchParam, resourceIdExpression));
        }

        if (parameterExpressions.Count == 0)
        {
            // No valid search parameters found - return expression that matches nothing
            return Expression.SearchParameter(
                context.SearchParameterManager.GetSearchParameter(resourceType, "_id"),
                Expression.Equals(FieldName.TokenCode, componentIndex: null, value: "compartment-no-match-impossible-id"));
        }

        if (parameterExpressions.Count == 1)
        {
            // Single parameter - return it directly
            return parameterExpressions[0];
        }

        // Multiple parameters - OR them together
        // Example: (subject = Patient/123) OR (performer = Patient/123)
        return Expression.Or(parameterExpressions.ToArray());
    }
}
