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
/// Example: CompartmentSearchExpression("Patient", "123") for Observation
/// Rewrites to: (subject = Patient/123) OR (performer = Patient/123)
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

        // Validate compartment ID is not empty (matches old implementation line 56-59)
        if (string.IsNullOrWhiteSpace(expression.CompartmentId))
        {
            throw new InvalidSearchOperationException($"Compartment ID is invalid or empty");
        }

        // Convert compartment type string to enum (e.g., "Patient" -> CompartmentType.Patient)
        if (!Enum.TryParse<CompartmentType>(expression.CompartmentType, out var compartmentType))
        {
            throw new InvalidSearchOperationException($"Compartment type '{expression.CompartmentType}' is invalid. Must be one of: Patient, Practitioner, RelatedPerson, Device, Encounter");
        }

        // Get search parameters that define compartment membership for this resource type
        if (!context.CompartmentManager.TryGetSearchParams(context.ResourceType, compartmentType, out var compartmentSearchParams))
        {
            // Resource type is not in this compartment - return expression that matches nothing
            // Use a non-existent ID to ensure no results match
            return Expression.SearchParameter(
                context.SearchParameterManager.GetSearchParameter(context.ResourceType, "_id"),
                Expression.Equals(FieldName.TokenCode, componentIndex: null, value: "compartment-no-match-impossible-id"));
        }

        // Build reference value: "Patient/123"
        string compartmentReference = $"{expression.CompartmentType}/{expression.CompartmentId}";

        // Create search parameter expressions for each compartment membership parameter
        var parameterExpressions = new List<Expression>();

        foreach (string searchParamCode in compartmentSearchParams)
        {
            SearchParameterInfo searchParam;
            try
            {
                searchParam = context.SearchParameterManager.GetSearchParameter(context.ResourceType, searchParamCode);
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
            // Use AND logic to match BOTH resourceType AND resourceId (more precise than full reference match)
            var binaryExpression = Expression.And(
                Expression.StringEquals(FieldName.ReferenceResourceType, componentIndex: null, value: expression.CompartmentType, ignoreCase: false),
                Expression.StringEquals(FieldName.ReferenceResourceId, componentIndex: null, value: expression.CompartmentId, ignoreCase: false));

            // Wrap in SearchParameterExpression
            parameterExpressions.Add(
                Expression.SearchParameter(searchParam, binaryExpression));
        }

        if (parameterExpressions.Count == 0)
        {
            // No valid search parameters found - return expression that matches nothing
            return Expression.SearchParameter(
                context.SearchParameterManager.GetSearchParameter(context.ResourceType, "_id"),
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
