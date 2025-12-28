// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Ignixa.Specification.ValueSets.Normative;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Abstractions;

namespace Ignixa.Search.Indexing;

/// <summary>
/// Provides a mechanism to create search indices.
/// </summary>
public partial class ElementSearchIndexer : ISearchIndexer
{
    private readonly IElementToSearchValueConverterManager _fhirElementTypeConverterManager;
    private readonly ILogger<ElementSearchIndexer> _logger;
    private readonly IReferenceToElementResolver _referenceToElementResolver;
    private readonly ISupportedSearchParameterDefinitionManager _searchParameterDefinitionManager;
    private readonly ConcurrentDictionary<string, List<string>> _targetTypesLookup = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ElementSearchIndexer"/> class.
    /// </summary>
    /// <param name="searchParameterDefinitionManager">The search parameter definition manager.</param>
    /// <param name="fhirElementTypeConverterManager">The FHIR element type converter manager.</param>
    /// <param name="referenceToElementResolver">Used for parsing reference strings</param>
    /// <param name="logger">The logger.</param>
    public ElementSearchIndexer(
        ISupportedSearchParameterDefinitionManager searchParameterDefinitionManager,
        IElementToSearchValueConverterManager fhirElementTypeConverterManager,
        IReferenceToElementResolver referenceToElementResolver,
        ILogger<ElementSearchIndexer> logger)
    {
        EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
        EnsureArg.IsNotNull(fhirElementTypeConverterManager, nameof(fhirElementTypeConverterManager));
        EnsureArg.IsNotNull(referenceToElementResolver, nameof(referenceToElementResolver));
        EnsureArg.IsNotNull(logger, nameof(logger));

        _searchParameterDefinitionManager = searchParameterDefinitionManager;
        _fhirElementTypeConverterManager = fhirElementTypeConverterManager;
        _referenceToElementResolver = referenceToElementResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<SearchIndexEntry> Extract(IElement resource)
    {
        EnsureArg.IsNotNull(resource, nameof(resource));

        var entries = new List<SearchIndexEntry>();

        var context = new FhirEvaluationContext();

        // Our FhirEvaluationContext uses IElement directly - no need for ToPocoNode()
        context.ElementResolver = str => _referenceToElementResolver.Resolve(str);

        // This allows resolving %resource FhirPath to provided value
        context.Resource = resource;

        IEnumerable<SearchParameterInfo> searchParameters = _searchParameterDefinitionManager.GetSearchParameters(resource.InstanceType);

        foreach (SearchParameterInfo searchParameter in searchParameters)
        {
            // Skip resource-table parameters (those handled at database/query level)
            // These are queried directly against the Resource table, not indexed in search parameter tables
            if (searchParameter.Code is "_id" or "_lastUpdated" or "_type")
                continue;

            if (searchParameter.Type == SearchParamType.Composite)
                entries.AddRange(ProcessCompositeSearchParameter(searchParameter, resource, context));
            else
                entries.AddRange(ProcessNonCompositeSearchParameter(searchParameter, resource, context));
        }

        return entries;
    }

    private IEnumerable<SearchIndexEntry> ProcessCompositeSearchParameter(SearchParameterInfo searchParameter, IElement resource, EvaluationContext context)
    {
        Debug.Assert(searchParameter?.Type == SearchParamType.Composite, "The search parameter must be composite.");

        SearchParameterInfo compositeSearchParameterInfo = searchParameter;

        IEnumerable<IElement> rootObjects = resource.Select(searchParameter.Expression, context);

        foreach (IElement rootObject in rootObjects)
        {
            int numberOfComponents = searchParameter.Component.Count;
            bool skip = false;

            var componentValues = new IReadOnlyList<ISearchValue>[numberOfComponents];

            // For each object extracted from the expression, we will need to evaluate each component.
            for (int i = 0; i < numberOfComponents; i++)
            {
                SearchParameterComponentInfo component = searchParameter.Component[i];

                // First find the type of the component.
                SearchParameterInfo componentSearchParameterDefinition = searchParameter.Component[i].ResolvedSearchParameter;

                // Skip if the component's search parameter is not resolved
                if (componentSearchParameterDefinition == null)
                {
                    Log.ComponentNullResolvedSearchParameter(_logger, i, searchParameter.Code);
                    skip = true;
                    break;
                }

                // Skip if the component expression is null or empty
                if (string.IsNullOrEmpty(component.Expression))
                {
                    Log.ComponentNullOrEmptyExpression(_logger, i, searchParameter.Code);
                    skip = true;
                    break;
                }

                IReadOnlyList<ISearchValue> extractedComponentValues = ExtractCompositeComponentSearchValues(
                    componentSearchParameterDefinition.Url.ToString(),
                    componentSearchParameterDefinition.Type,
                    componentSearchParameterDefinition.TargetResourceTypes,
                    rootObject,
                    component.Expression,
                    context);

                // Filter out any search value that's not valid as a composite component.
                extractedComponentValues = extractedComponentValues
                    .Where(sv => sv.IsValidAsCompositeComponent)
                    .ToArray();

                if (!extractedComponentValues.Any())
                {
                    // One of the components didn't have any value and therefore it will not be indexed.
                    skip = true;
                    break;
                }

                componentValues[i] = extractedComponentValues;
            }

            if (skip) continue;

            yield return new SearchIndexEntry(compositeSearchParameterInfo, new CompositeSearchValue(componentValues));
        }
    }

    private IEnumerable<SearchIndexEntry> ProcessNonCompositeSearchParameter(SearchParameterInfo searchParameter, IElement resource, EvaluationContext context)
    {
        EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
        Debug.Assert(searchParameter.Type != SearchParamType.Composite, "The search parameter must be non-composite.");

        // Skip indexing for search parameters with empty or whitespace expressions
        if (string.IsNullOrWhiteSpace(searchParameter.Expression))
        {
            yield break;
        }

        SearchParameterInfo searchParameterInfo = searchParameter;

        foreach (ISearchValue searchValue in ExtractSearchValues(
                     searchParameter.Url.ToString(),
                     searchParameter.Type,
                     searchParameter.TargetResourceTypes,
                     resource,
                     searchParameter.Expression,
                     context))
            yield return new SearchIndexEntry(searchParameterInfo, searchValue);
    }

    private IReadOnlyList<ISearchValue> ExtractCompositeComponentSearchValues(
        string searchParameterDefinitionUrl,
        SearchParamType? componentDefinitionType,
        IReadOnlyList<string> allowedReferenceResourceTypes,
        IElement element,
        string fhirPathExpression,
        EvaluationContext context)
    {
        // Use the component definition type to determine the search value type.
        // This ensures consistency between indexing and querying.
        // Only fall back to type inference if the definition type doesn't work.

        var results = new List<ISearchValue>();

        IEnumerable<IElement> extractedValues = Enumerable.Empty<IElement>();

        try
        {
            extractedValues = element.Select(fhirPathExpression, context);
        }
        catch (Exception ex)
        {
            Log.FailedToExtractValues(_logger, ex, fhirPathExpression, element.InstanceType, searchParameterDefinitionUrl);
        }

        Debug.Assert(extractedValues != null, "The extracted values should not be null.");

        foreach (IElement extractedValue in extractedValues)
        {
            if (string.IsNullOrEmpty(extractedValue.InstanceType))
            {
                Log.SkippingElementNullOrEmptyInstanceType(_logger);
                continue;
            }

            // First, try using the component definition type (preferred approach)
            SearchParamType? effectiveType = componentDefinitionType;
            IElementToSearchValueConverter converter = null;

            if (effectiveType.HasValue)
            {
                _fhirElementTypeConverterManager.TryGetConverter(
                    extractedValue.InstanceType,
                    GetSearchValueTypeForSearchParamType(effectiveType),
                    out converter);
            }

            // If the definition type didn't work, fall back to type inference
            // This handles edge cases like DocumentReference "relationship" parameter
            if (converter == null)
            {
                effectiveType = InferSearchParamTypeFromFhirType(extractedValue.InstanceType);

                if (!effectiveType.HasValue)
                {
                    Log.CannotInferSearchParamType(_logger, extractedValue.InstanceType, searchParameterDefinitionUrl);
                    continue;
                }

                if (!_fhirElementTypeConverterManager.TryGetConverter(
                    extractedValue.InstanceType,
                    GetSearchValueTypeForSearchParamType(effectiveType),
                    out converter))
                {
                    Log.FhirElementTypeNotSupported(_logger, extractedValue.InstanceType);
                    continue;
                }
            }

            IEnumerable<ISearchValue> searchValues = converter.ConvertTo(extractedValue);

            if (searchValues != null)
            {
                // For reference components with a single allowed resource type, set the type if not specified
                if (effectiveType == SearchParamType.Reference && allowedReferenceResourceTypes?.Count == 1)
                {
                    string singleAllowedResourceType = allowedReferenceResourceTypes[0];
                    foreach (ISearchValue searchValue in searchValues)
                    {
                        if (searchValue == null)
                            continue;

                        if (searchValue is ReferenceSearchValue rsr && string.IsNullOrEmpty(rsr.ResourceType))
                            results.Add(new ReferenceSearchValue(rsr.Kind, rsr.BaseUri, singleAllowedResourceType, rsr.ResourceId));
                        else
                            results.Add(searchValue);
                    }
                }
                else
                {
                    results.AddRange(searchValues.Where(sv => sv != null));
                }
            }
        }

        return results;
    }

    private IReadOnlyList<ISearchValue> ExtractSearchValues(
        string searchParameterDefinitionUrl,
        SearchParamType? searchParameterType,
        IReadOnlyList<string> allowedReferenceResourceTypes,
        IElement element,
        string fhirPathExpression,
        EvaluationContext context)
    {
        Debug.Assert(searchParameterType != SearchParamType.Composite, "The search parameter must be non-composite.");

        var results = new List<ISearchValue>();

        // For simple value type, we can parse the expression directly.
        IEnumerable<IElement> extractedValues = Enumerable.Empty<IElement>();

        try
        {
            extractedValues = element.Select(fhirPathExpression, context);
        }
        catch (Exception ex)
        {
            Log.FailedToExtractValues(_logger, ex, fhirPathExpression, element.InstanceType, searchParameterDefinitionUrl);
        }

        Debug.Assert(extractedValues != null, "The extracted values should not be null.");

        // If there is target set, then filter the extracted values to only those types.
        if (searchParameterType == SearchParamType.Reference &&
            allowedReferenceResourceTypes?.Count > 0)
        {
            List<string> targetResourceTypes = _targetTypesLookup.GetOrAdd(searchParameterDefinitionUrl, _ =>
            {
                return allowedReferenceResourceTypes.Select(t => t.ToString()).ToList();
            });

            // TODO: The expression for reference search parameters in Stu3 has issues.
            // The reference search parameter could be pointing to an element that can be multiple types. For example,
            // the Appointment.participant.actor can be type of Patient, Practitioner, Related Person, Location, and so on.
            // Some search parameter could refer to this property but restrict to certain types. For example,
            // Appointment's location search parameter is returned only when Appointment.participant.actor is Location element.
            // The Stu3 expressions don't have this restriction so everything is being returned. This is addressed in R4 release (see
            // http://community.fhir.org/t/expression-seems-incorrect-for-reference-search-parameter-thats-only-applicable-to-certain-types/916/2).
            // Therefore, for now, we will need to compare the reference value itself (which can be internal or external references), and restrict
            // the values ourselves.
            extractedValues = extractedValues.Where(ev =>
            {
                if (ev.InstanceType != null &&
                    ev.InstanceType.Equals("ResourceReference", StringComparison.OrdinalIgnoreCase))
                {
                    return ev.Scalar("reference") is string rr && targetResourceTypes.Any(trt => rr.Contains(trt, StringComparison.Ordinal));
                }

                return true;
            });
        }

        foreach (IElement extractedValue in extractedValues)
        {
            if (string.IsNullOrEmpty(extractedValue.InstanceType))
            {
                Log.SkippingElementNullOrEmptyInstanceType(_logger);
                continue;
            }

            if (!_fhirElementTypeConverterManager.TryGetConverter(extractedValue.InstanceType, GetSearchValueTypeForSearchParamType(searchParameterType), out IElementToSearchValueConverter converter))
            {
                Log.FhirElementTypeNotSupported(_logger, extractedValue.InstanceType);

                continue;
            }

            IEnumerable<ISearchValue> searchValues = converter.ConvertTo(extractedValue);

            if (searchValues != null)
            {
                if (searchParameterType == SearchParamType.Reference && allowedReferenceResourceTypes?.Count == 1)
                {
                    // For references, if the type is not specified in the reference string, we can set the type on the search value because
                    // in this case it can only be of one type.
                    string singleAllowedResourceType = allowedReferenceResourceTypes[0];
                    foreach (ISearchValue searchValue in searchValues)
                    {
                        if (searchValue == null)
                            continue;

                        if (searchValue is ReferenceSearchValue rsr && string.IsNullOrEmpty(rsr.ResourceType))
                            results.Add(new ReferenceSearchValue(rsr.Kind, rsr.BaseUri, singleAllowedResourceType, rsr.ResourceId));
                        else
                            results.Add(searchValue);
                    }
                }
                else
                {
                    // Filter out any null values that converters might return
                    results.AddRange(searchValues.Where(sv => sv != null));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Infers the appropriate SearchParamType from a FHIR element type.
    /// This is used for composite components where the component definition's type may not match
    /// the actual extracted value's type due to FHIR spec inconsistencies.
    /// </summary>
    internal static SearchParamType? InferSearchParamTypeFromFhirType(string fhirType)
    {
        return fhirType switch
        {
            // Reference types
            "Reference" or "ResourceReference" => SearchParamType.Reference,

            // Token types
            "code" or "codeOfT" or "System.Code" or "Coding" or "CodeableConcept" or "Identifier"
                or "ContactPoint" or "boolean" or "id" => SearchParamType.Token,

            // String types
            "string" or "HumanName" or "Address" or "markdown" => SearchParamType.String,

            // Number types
            "integer" or "decimal" => SearchParamType.Number,

            // Date types
            "date" or "dateTime" or "instant" or "Period" => SearchParamType.Date,

            // Quantity types
            "Quantity" or "Money" or "Range" => SearchParamType.Quantity,

            // Uri types
            "uri" or "url" or "canonical" or "oid" => SearchParamType.Uri,

            // CodeableReference can be either token or reference depending on context
            // Default to token for composite components as it's more common
            "CodeableReference" => SearchParamType.Token,

            // Unknown type - return null to indicate we can't infer
            _ => null
        };
    }

    internal static Type GetSearchValueTypeForSearchParamType(SearchParamType? searchParamType)
    {
        switch (searchParamType)
        {
            case SearchParamType.Number:
                return typeof(NumberSearchValue);
            case SearchParamType.Date:
                return typeof(DateTimeSearchValue);
            case SearchParamType.String:
                return typeof(StringSearchValue);
            case SearchParamType.Token:
                return typeof(TokenSearchValue);
            case SearchParamType.Reference:
                return typeof(ReferenceSearchValue);
            case SearchParamType.Composite:
                return typeof(CompositeSearchValue);
            case SearchParamType.Quantity:
                return typeof(QuantitySearchValue);
            case SearchParamType.Uri:
                return typeof(UriSearchValue);
            case SearchParamType.Special:
                return typeof(StringSearchValue);
            default:
                throw new ArgumentOutOfRangeException(nameof(searchParamType), searchParamType, null);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Component {ComponentIndex} of composite search parameter '{SearchParameterCode}' has null ResolvedSearchParameter. Skipping this composite value.")]
        public static partial void ComponentNullResolvedSearchParameter(ILogger logger, int componentIndex, string searchParameterCode);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Component {ComponentIndex} of composite search parameter '{SearchParameterCode}' has null or empty Expression. Skipping this composite value.")]
        public static partial void ComponentNullOrEmptyExpression(ILogger logger, int componentIndex, string searchParameterCode);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to extract the values using '{FhirPathExpression}' against '{ElementType}' for search parameter '{SearchParameterUrl}'.")]
        public static partial void FailedToExtractValues(ILogger logger, Exception ex, string fhirPathExpression, string elementType, string searchParameterUrl);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping element with null or empty InstanceType during search indexing.")]
        public static partial void SkippingElementNullOrEmptyInstanceType(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "The FHIR element '{ElementType}' is not supported.")]
        public static partial void FhirElementTypeNotSupported(ILogger logger, string elementType);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot infer SearchParamType from FHIR element type '{FhirElementType}' for composite component of search parameter '{SearchParameterUrl}'. Skipping this value.")]
        public static partial void CannotInferSearchParamType(ILogger logger, string fhirElementType, string searchParameterUrl);
    }
}
