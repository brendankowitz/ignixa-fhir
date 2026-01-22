/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FHIR-specific FhirPath function implementations.
 * Implements extension(), resolve(), getResourceKey(), getReferenceKey(), comparable().
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Types;

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
    [FhirPathFunction("extension",
        SupportedContexts = "any-any",
        ReturnType = "Extension",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        Category = "FHIR",
        Description = "Filters extensions by URL")]
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

        // Non-scoped function: evaluate argument in outer context (don't change $this)
        var urlArgument = arguments[0];
        var urlResult = evaluateExpression(context.Focus, urlArgument, context).FirstOrDefault();

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
    [FhirPathFunction("resolve",
        SupportedContexts = "Reference-Resource",
        ReturnType = "Resource",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "FHIR",
        Description = "Resolves a Reference to the actual resource")]
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
            string? referenceValue = null;

            // resolve() works on Reference types
            if (element.InstanceType == "Reference" || element.InstanceType == "ResourceReference")
            {
                // Try to extract the reference string from the "reference" child element
                referenceValue = element.Scalar("reference") as string;

                // If no nested child, check if Value is the reference string directly
                // This happens when navigating to .reference which returns the Reference with its value
                if (string.IsNullOrEmpty(referenceValue) && element.Value is string valueStr)
                {
                    referenceValue = valueStr;
                }
            }
            // Also handle string values directly (common in FHIRPath expressions like entry.reference.where(resolve() is ...))
            else if (element.InstanceType is "string" or "uri" or "canonical" or "url" && element.Value is string strValue)
            {
                referenceValue = strValue;
            }
            else
            {
                // Not a reference - skip
                continue;
            }
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
    [FhirPathFunction("getResourceKey",
        SupportedContexts = "any-string",
        ReturnType = "string",
        SupportedAtRoot = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "FHIR",
        Description = "Returns resourceType/id for the root resource")]
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
    [FhirPathFunction("getReferenceKey",
        SupportedContexts = "Reference-string",
        ReturnType = "string",
        MinArguments = 0,
        MaxArguments = 1,
        Category = "FHIR",
        Description = "Extracts reference key from a Reference element")]
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
            // Note: Due to parser behavior, bare identifiers may be parsed as PropertyAccessExpression
            if (arguments[0] is IdentifierExpression identExpr)
            {
                filterType = identExpr.Name;
            }
            else if (arguments[0] is PropertyAccessExpression propExpr)
            {
                // Bare identifiers like "Patient" are parsed as PropertyAccessExpression with null focus
                filterType = propExpr.PropertyName;
            }
            else if (arguments[0] is FunctionCallExpression funcExpr && funcExpr.Arguments.Count == 0)
            {
                // Sometimes bare identifiers are parsed as zero-argument function calls
                filterType = funcExpr.FunctionName;
            }
            else
            {
                // Non-scoped function: evaluate argument in outer context (don't change $this)
                var result = evaluateExpression(context.Focus, arguments[0], context).ToList();
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

    /// <summary>
    /// comparable() - Returns true if the input quantity can be compared to the argument quantity.
    /// Two quantities are comparable if their units have the same UCUM dimension (e.g., cm and [in_i] are both lengths).
    /// Per FHIRPath spec: Returns true if the quantities have compatible units per UCUM.
    /// </summary>
    [FhirPathFunction("comparable",
        SupportedContexts = "Quantity-Quantity",
        ReturnType = "boolean",
        MinArguments = 1,
        MaxArguments = 1,
        TakesExpressionArguments = true,
        Category = "FHIR",
        Description = "Returns true if two quantities have comparable units")]
    public static IEnumerable<IElement> Comparable(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        // comparable(other : Quantity) : Boolean
        // Returns true if the input quantity can be compared to the argument quantity.
        
        var focusList = focus.ToList();
        if (focusList.Count != 1)
        {
            // Per FHIRPath: If input is not a single quantity, return empty
            yield break;
        }

        var inputElement = focusList[0];
        
        // Evaluate the argument
        if (arguments.Count == 0)
        {
            yield break;
        }
        
        var argResults = evaluateExpression(context.Focus, arguments[0], context).ToList();
        if (argResults.Count != 1)
        {
            // Per FHIRPath: If argument is not a single quantity, return empty
            yield break;
        }

        var argElement = argResults[0];
        
        // Extract units from both quantities
        var inputUnit = ExtractUnitFromQuantity(inputElement);
        var argUnit = ExtractUnitFromQuantity(argElement);
        
        if (inputUnit == null || argUnit == null)
        {
            // Cannot determine units - return empty
            yield break;
        }

        // Check compatibility using the UCUM converter
        var converter = QuantityUnitConverter.Instance;
        var areCompatible = converter.IsCompatible(inputUnit, argUnit);
        
        yield return FunctionHelpers.CreateBoolean(areCompatible);
    }

    /// <summary>
    /// Extracts the unit from a Quantity element.
    /// </summary>
    private static string? ExtractUnitFromQuantity(IElement element)
    {
        // Handle Quantity type from our Types namespace
        if (element.Value is Quantity qty)
        {
            return qty.Unit;
        }

        // Handle FHIR Quantity element with children
        if (element.InstanceType == "Quantity")
        {
            // Try 'code' first (UCUM code)
            var codeChildren = element.Children("code");
            if (codeChildren.Count > 0 && codeChildren[0].Value is string code && !string.IsNullOrEmpty(code))
            {
                return code;
            }
            
            // Fall back to 'unit' (display name, but might also be a UCUM code)
            var unitChildren = element.Children("unit");
            if (unitChildren.Count > 0 && unitChildren[0].Value is string unit && !string.IsNullOrEmpty(unit))
            {
                return unit;
            }
        }

        return null;
    }

    #region Not Supported Functions

    /// <summary>
    /// conformsTo() - Tests whether a resource conforms to a StructureDefinition.
    /// NOT SUPPORTED: Requires profile validation infrastructure.
    /// </summary>
    [FhirPathFunction("conformsTo",
        SupportedContexts = "Resource-Resource",
        ReturnType = "boolean",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "FHIR",
        Description = "Tests whether a resource conforms to a StructureDefinition (NOT SUPPORTED)")]
    public static IEnumerable<IElement> ConformsTo(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        throw new NotSupportedException("Function 'conformsTo' is not supported. It requires profile validation infrastructure.");
    }

    /// <summary>
    /// memberOf() - Tests whether a code is in a value set.
    /// NOT SUPPORTED: Requires terminology service.
    /// </summary>
    [FhirPathFunction("memberOf",
        SupportedContexts = "code-Coding-CodeableConcept",
        ReturnType = "boolean",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "FHIR",
        Description = "Tests whether a code is in a value set (NOT SUPPORTED)")]
    public static IEnumerable<IElement> MemberOf(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        throw new NotSupportedException("Function 'memberOf' is not supported. It requires terminology service integration.");
    }

    /// <summary>
    /// validateVS() - Validates a code against a value set.
    /// NOT SUPPORTED: Requires terminology service.
    /// </summary>
    [FhirPathFunction("validateVS",
        SupportedContexts = "code-Coding-CodeableConcept",
        ReturnType = "boolean",
        MinArguments = 1,
        MaxArguments = 2,
        Category = "FHIR",
        Description = "Validates a code against a value set (NOT SUPPORTED)")]
    public static IEnumerable<IElement> ValidateVS(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        throw new NotSupportedException("Function 'validateVS' is not supported. It requires terminology service integration.");
    }

    /// <summary>
    /// translate() - Translates a code from one value set to another using a ConceptMap.
    /// NOT SUPPORTED: Requires terminology service.
    /// </summary>
    [FhirPathFunction("translate",
        SupportedContexts = "code-Coding-CodeableConcept",
        ReturnType = "Coding",
        MinArguments = 2,
        MaxArguments = 3,
        Category = "FHIR",
        Description = "Translates a code using a ConceptMap (NOT SUPPORTED)")]
    public static IEnumerable<IElement> Translate(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        throw new NotSupportedException("Function 'translate' is not supported. It requires terminology service integration.");
    }

    /// <summary>
    /// hasTemplateIdOf() - CDA-specific function to check template IDs.
    /// NOT SUPPORTED: CDA support is out of scope.
    /// </summary>
    [FhirPathFunction("hasTemplateIdOf",
        SupportedContexts = "any-any",
        ReturnType = "boolean",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "CDA",
        Description = "CDA-specific template ID check (NOT SUPPORTED)")]
    public static IEnumerable<IElement> HasTemplateIdOf(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        throw new NotSupportedException("Function 'hasTemplateIdOf' is not supported. CDA support is out of scope.");
    }

    #endregion
}
