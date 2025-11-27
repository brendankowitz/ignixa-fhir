/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FHIR-specific FhirPath function implementations.
 * Implements extension(), resolve(), getResourceKey(), getReferenceKey().
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// FHIR-specific function implementations for FhirPath expressions.
/// </summary>
internal static class FhirSpecificFunctions
{
    /// <summary>
    /// extension() - Filters the input collection for items named "extension" with the given url.
    /// Equivalent to: .extension.where(url = urlValue)
    /// </summary>
    public static IEnumerable<IElement> Extension(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        // extension(url : string) : collection
        // Filters the input collection for items named "extension" with the given url
        // Equivalent to: .extension.where(url = <urlValue>)

        if (arguments.Count == 0)
            throw new ArgumentException("extension() requires a url argument");

        // Evaluate the url argument to get the string value
        var urlArgument = arguments[0];
        var urlResult = evaluateExpression(focus, urlArgument, context).FirstOrDefault();

        if (urlResult == null)
            yield break;

        var urlValue = urlResult.Value?.ToString();
        if (string.IsNullOrEmpty(urlValue))
            yield break;

        // Navigate to "extension" children and filter by url
        foreach (var element in focus)
        {
            foreach (var extension in element.Children("extension"))
            {
                // Check if this extension has a url child with matching value
                var urlChildren = extension.Children("url");
                if (urlChildren.Count > 0 && urlChildren[0].Value?.ToString() == urlValue)
                {
                    yield return extension;
                }
            }
        }
    }

    /// <summary>
    /// resolve() - Takes a Reference element and resolves it to the actual resource.
    /// Returns empty if the reference cannot be resolved or if ElementResolver is not configured.
    /// Per FHIR spec: resolve() returns empty on failure (does not throw).
    /// </summary>
    public static IEnumerable<IElement> Resolve(
        IEnumerable<IElement> focus,
        EvaluationContext context)
    {
        // resolve() : collection
        // Takes a Reference element and resolves it to the actual resource.
        // Returns empty if the reference cannot be resolved or if ElementResolver is not configured.
        // Per FHIR spec: resolve() returns empty on failure (does not throw).

        var results = new List<IElement>();

        if (context is not FhirEvaluationContext fhirContext || fhirContext.ElementResolver == null)
        {
            // No resolver available - return empty (this is expected during indexing)
            return results;
        }

        foreach (var element in focus)
        {
            // resolve() only works on Reference types
            if (element.InstanceType != "Reference" && element.InstanceType != "ResourceReference")
            {
                // Not a reference - skip
                continue;
            }

            // Extract the reference string (e.g., "Patient/123" or "http://example.org/fhir/Patient/123")
            var referenceValue = element.Scalar("reference") as string;
            if (string.IsNullOrEmpty(referenceValue))
            {
                // No reference value - skip
                continue;
            }

            // Call the ElementResolver to resolve the reference
            try
            {
                var resolved = fhirContext.ElementResolver(referenceValue);
                if (resolved != null)
                {
                    results.Add(resolved);
                }
                // If resolved is null, the reference couldn't be resolved - skip silently
            }
            catch
            {
                // If resolution fails, skip silently (FHIR spec: resolve() returns empty on failure)
                continue;
            }
        }

        return results;
    }

    /// <summary>
    /// getResourceKey() - SQL on FHIR v2 function that returns resourceType/id for the ROOT resource.
    /// Enables JOINs across resources and should always reference the root, not the current focus.
    /// </summary>
    public static IEnumerable<IElement> GetResourceKey(EvaluationContext context)
    {
        // Per SQL on FHIR v2 spec: getResourceKey() returns "{resourceType}/{id}" for the ROOT resource
        // This enables JOINs across resources and should always reference the root, not the current focus
        var rootResource = context.RootResource ?? context.Resource;
        if (rootResource == null)
        {
            yield break; // No root resource available
        }

        // Get resource type from InstanceType
        var resourceType = rootResource.InstanceType;
        if (string.IsNullOrEmpty(resourceType))
        {
            yield break; // No resource type
        }

        // Get id from the "id" child element
        var idChildren = rootResource.Children("id");
        if (idChildren.Count == 0)
        {
            yield break; // No id
        }

        var idElement = idChildren[0];

        var id = idElement.Value?.ToString();
        if (string.IsNullOrEmpty(id))
        {
            yield break; // Empty id
        }

        // Return "{resourceType}/{id}"
        var resourceKey = $"{resourceType}/{id}";
        yield return FunctionHelpers.CreateString(resourceKey);
    }

    /// <summary>
    /// getReferenceKey() - SQL on FHIR v2 function that extracts reference from a Reference element.
    /// Returns the full reference string (e.g., "Patient/123").
    /// Optional type parameter filters by resource type.
    /// </summary>
    public static IEnumerable<IElement> GetReferenceKey(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        // Per SQL on FHIR v2 spec: getReferenceKey([type]) extracts reference from a Reference element
        // Returns the full reference string (e.g., "Patient/123")
        // Optional type parameter filters by resource type - returns empty if type doesn't match

        // Parse optional type argument (matches ofType() implementation pattern)
        string? filterType = null;
        if (arguments.Count > 0)
        {
            // Type argument should be a simple identifier (e.g., "Patient", "Observation")
            if (arguments[0] is IdentifierExpression identExpr)
            {
                filterType = identExpr.Name;
            }
            else if (arguments[0] is FunctionCallExpression funcExpr && funcExpr.Arguments.Count == 0)
            {
                // Sometimes bare identifiers are parsed as zero-argument function calls
                filterType = funcExpr.FunctionName;
            }
            else
            {
                // Fallback: evaluate the expression to get the type name
                var result = evaluateExpression(focus, arguments[0], context).ToList();
                if (result.Count > 0)
                {
                    filterType = result[0].Value?.ToString();
                }
            }

            // If we couldn't determine the filter type, return empty
            if (string.IsNullOrEmpty(filterType))
            {
                yield break;
            }
        }

        foreach (var element in focus)
        {
            // Get the "reference" child element from the Reference datatype
            var referenceChildren = element.Children("reference");
            if (referenceChildren.Count == 0)
            {
                continue; // Skip if no reference property
            }

            var referenceElement = referenceChildren[0];

            var reference = referenceElement.Value?.ToString();
            if (string.IsNullOrEmpty(reference))
            {
                continue; // Skip if reference is empty
            }

            // If type filter specified, check if reference matches the type
            if (filterType != null)
            {
                // Check if reference starts with "{type}/" (e.g., "Patient/123")
                var expectedPrefix = $"{filterType}/";
                if (!reference.StartsWith(expectedPrefix, StringComparison.Ordinal))
                {
                    // Type mismatch - skip this reference (don't yield anything)
                    continue;
                }
            }

            // Return the full reference string (e.g., "Patient/123")
            // This matches the format returned by getResourceKey()
            yield return FunctionHelpers.CreateString(reference);
        }
    }
}
