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

        // Schema is deliberately left unset. That omission is load-bearing: it is the only thing keeping
        // TypeMatcher's two schema-gated errors off the write path. Both no-op on a null schema -
        // EnsureTypeIdentifierResolves returns early, and EnsureSingletonInput's EnforcesSingletonCast
        // requires a non-null schema at R5 or later - so setting Schema here arms both for every
        // expression the indexer evaluates, and ProcessNonCompositeSearchParameter logs and continues,
        // which means anything they throw becomes a search parameter that silently indexes nothing.
        //
        // Measured, not assumed: doing this today costs no index entries. The one R5 expression that
        // casts a repeating path with `as` is AdverseEvent-substance, and it already yields zero entries
        // because suspectEntity.instance resolves as CodeableConcept, so `as Reference` matches nothing
        // whether or not the rule is armed. The cost is latent rather than current, and it is of two
        // kinds. First, arming EnsureTypeIdentifierResolves makes every type identifier in every shipped
        // and custom search parameter a potential write-path failure, which is a far wider blast radius
        // than the one parameter above and is not covered by tests. Second, and more insidiously, it
        // puts a tripwire under the CodeableReference fix: once instance resolves as Reference the cast
        // matches 2 items, and an armed singleton rule turns that recovered data straight back into a
        // swallowed exception. See the remarks on TypeMatcher.EnsureSingletonInput for the full
        // interaction. Do not set Schema here without first exempting indexing from the singleton rule
        // by some other means.
        var context = new FhirEvaluationContext
        {
            ElementResolver = str => _referenceToElementResolver.Resolve(str),
            Resource = resource
        };

        IEnumerable<SearchParameterInfo> searchParameters = _searchParameterDefinitionManager.GetSearchParameters(resource.InstanceType);

        // Resolved once, up front: every failure below is logged per search parameter, and without this the
        // logs name an expression and a type but not which resource carried them, so a resource left
        // permanently unindexed cannot be found again to reindex it.
        string resourceIdentity = DescribeResource(resource);

        foreach (SearchParameterInfo searchParameter in searchParameters)
        {
            // Intrinsic parameters are read from the resource record itself, so no index entry is emitted.
            if (IntrinsicSearchParameters.IsIntrinsicCode(searchParameter.Code))
                continue;

            if (searchParameter.Type == SearchParamType.Composite)
                entries.AddRange(ProcessCompositeSearchParameter(searchParameter, resource, context, resourceIdentity));
            else
                entries.AddRange(ProcessNonCompositeSearchParameter(searchParameter, resource, context, resourceIdentity));
        }

        MarkMinMaxValues(entries);

        return entries;
    }

    /// <summary>
    /// A resource's search parameter can have multiple values (e.g. multiple HumanName entries for
    /// Patient.name). This marks which of those values is the min and which is the max for each
    /// distinct search parameter, so a compiled sort can seek directly against IsMin/IsMax-flagged
    /// rows instead of aggregating at query time. Ported from microsoft/fhir-server's
    /// ResourceWrapperFactory.ExtractMinAndMaxValues -- see
    /// docs/superpowers/plans/2026-07-18-search-indexer-min-max-flags.md's Global Constraints for the
    /// exact source method this mirrors.
    /// </summary>
    internal static void MarkMinMaxValues(IReadOnlyCollection<SearchIndexEntry> searchIndices)
    {
        var minValues = new Dictionary<Uri, ISupportSortSearchValue>();
        var maxValues = new Dictionary<Uri, ISupportSortSearchValue>();

        foreach (SearchIndexEntry currentEntry in searchIndices)
        {
            if (currentEntry.Value is not ISupportSortSearchValue currentValue)
            {
                continue;
            }

            if (currentEntry.SearchParameter.SortStatus == SortParameterStatus.Disabled)
            {
                continue;
            }

            if (minValues.TryGetValue(currentEntry.SearchParameter.Url, out ISupportSortSearchValue existingMinValue))
            {
                if (currentValue.CompareTo(existingMinValue, ComparisonRange.Min) < 0)
                {
                    minValues[currentEntry.SearchParameter.Url] = currentValue;
                }
            }
            else
            {
                minValues.Add(currentEntry.SearchParameter.Url, currentValue);
            }

            if (maxValues.TryGetValue(currentEntry.SearchParameter.Url, out ISupportSortSearchValue existingMaxValue))
            {
                if (currentValue.CompareTo(existingMaxValue, ComparisonRange.Max) > 0)
                {
                    maxValues[currentEntry.SearchParameter.Url] = currentValue;
                }
            }
            else
            {
                maxValues.Add(currentEntry.SearchParameter.Url, currentValue);
            }
        }

        foreach (KeyValuePair<Uri, ISupportSortSearchValue> kvp in minValues)
        {
            kvp.Value.IsMin = true;
        }

        foreach (KeyValuePair<Uri, ISupportSortSearchValue> kvp in maxValues)
        {
            kvp.Value.IsMax = true;
        }
    }

    private IEnumerable<SearchIndexEntry> ProcessCompositeSearchParameter(SearchParameterInfo searchParameter, IElement resource, EvaluationContext context, string resourceIdentity)
    {
        Debug.Assert(searchParameter?.Type == SearchParamType.Composite, "The search parameter must be composite.");

        SearchParameterInfo compositeSearchParameterInfo = searchParameter;

        // Materialized inside the try for the same reason as the two ExtractSearchValues paths below: this
        // method is itself a yield iterator, so a lazy enumerable escaping here would not be enumerated until
        // Extract's entries.AddRange, well past this catch and outside ISearchIndexer.Extract's control -
        // turning one malformed composite expression into a failed create or update of the whole resource.
        IEnumerable<IElement> rootObjects = Enumerable.Empty<IElement>();

        try
        {
            rootObjects = resource.Select(searchParameter.Expression, context).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsExpectedEvaluationFailure(ex))
        {
            Log.FailedToExtractValues(_logger, ex, searchParameter.Expression, resource.InstanceType, searchParameter.Url.ToString(), resourceIdentity);
        }
        catch (Exception ex)
        {
            Log.UnexpectedExtractionFailure(_logger, ex, searchParameter.Expression, resource.InstanceType, searchParameter.Url.ToString(), resourceIdentity);
        }

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
                    context,
                    resourceIdentity);

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

            yield return new SearchIndexEntry(compositeSearchParameterInfo, new CompositeIndexSearchValue(componentValues));
        }
    }

    private IEnumerable<SearchIndexEntry> ProcessNonCompositeSearchParameter(SearchParameterInfo searchParameter, IElement resource, EvaluationContext context, string resourceIdentity)
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
                     context,
                     resourceIdentity))
            yield return new SearchIndexEntry(searchParameterInfo, searchValue);
    }

    private IReadOnlyList<ISearchValue> ExtractCompositeComponentSearchValues(
        string searchParameterDefinitionUrl,
        SearchParamType? componentDefinitionType,
        IReadOnlyList<string> allowedReferenceResourceTypes,
        IElement element,
        string fhirPathExpression,
        EvaluationContext context,
        string resourceIdentity)
    {
        // Use the component definition type to determine the search value type.
        // This ensures consistency between indexing and querying.
        // Only fall back to type inference if the definition type doesn't work.

        var results = new List<ISearchValue>();

        // Materialized inside the try: element.Select returns a lazy enumerable, so enumerating it
        // outside would raise evaluation errors past this catch and fail the whole write.
        IEnumerable<IElement> extractedValues = Enumerable.Empty<IElement>();

        try
        {
            extractedValues = element.Select(fhirPathExpression, context).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsExpectedEvaluationFailure(ex))
        {
            Log.FailedToExtractValues(_logger, ex, fhirPathExpression, element.InstanceType, searchParameterDefinitionUrl, resourceIdentity);
        }
        catch (Exception ex)
        {
            Log.UnexpectedExtractionFailure(_logger, ex, fhirPathExpression, element.InstanceType, searchParameterDefinitionUrl, resourceIdentity);
        }

        Debug.Assert(extractedValues != null, "The extracted values should not be null.");

        foreach (IElement extractedValue in extractedValues)
        {
            if (string.IsNullOrEmpty(extractedValue.InstanceType))
            {
                Log.SkippingElementNullOrEmptyInstanceType(_logger, searchParameterDefinitionUrl, resourceIdentity);
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
                    Log.FhirElementTypeNotSupported(_logger, extractedValue.InstanceType, searchParameterDefinitionUrl);
                    continue;
                }
            }

            IEnumerable<ISearchValue> searchValues = ConvertOrLog(converter, extractedValue, fhirPathExpression, searchParameterDefinitionUrl, resourceIdentity);

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
        EvaluationContext context,
        string resourceIdentity)
    {
        Debug.Assert(searchParameterType != SearchParamType.Composite, "The search parameter must be non-composite.");

        var results = new List<ISearchValue>();

        // For simple value type, we can parse the expression directly.
        // Materialized inside the try: element.Select returns a lazy enumerable, so enumerating it
        // outside would raise evaluation errors past this catch and fail the whole write.
        IEnumerable<IElement> extractedValues = Enumerable.Empty<IElement>();

        try
        {
            extractedValues = element.Select(fhirPathExpression, context).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsExpectedEvaluationFailure(ex))
        {
            Log.FailedToExtractValues(_logger, ex, fhirPathExpression, element.InstanceType, searchParameterDefinitionUrl, resourceIdentity);
        }
        catch (Exception ex)
        {
            Log.UnexpectedExtractionFailure(_logger, ex, fhirPathExpression, element.InstanceType, searchParameterDefinitionUrl, resourceIdentity);
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
                Log.SkippingElementNullOrEmptyInstanceType(_logger, searchParameterDefinitionUrl, resourceIdentity);
                continue;
            }

            if (!_fhirElementTypeConverterManager.TryGetConverter(extractedValue.InstanceType, GetSearchValueTypeForSearchParamType(searchParameterType), out IElementToSearchValueConverter converter))
            {
                Log.FhirElementTypeNotSupported(_logger, extractedValue.InstanceType, searchParameterDefinitionUrl);

                continue;
            }

            IEnumerable<ISearchValue> searchValues = ConvertOrLog(converter, extractedValue, fhirPathExpression, searchParameterDefinitionUrl, resourceIdentity);

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
            "date" or "dateTime" or "instant" or "Period" or "Timing" => SearchParamType.Date,

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

    /// <summary>
    /// Runs a converter and contains anything it throws to the one value being converted.
    /// <para>
    /// Converters are free to return a lazy sequence - several are <c>yield</c> iterators, so no conversion
    /// work happens until the caller enumerates. That enumeration used to sit outside the try guarding
    /// <c>element.Select</c>, which meant a single malformed literal (a <c>Timing.event</c> that
    /// <c>PartialDateTime.Parse</c> rejects, say) escaped the indexer and failed the entire create or update.
    /// Materializing here puts every converter, lazy or eager, inside this catch.
    /// </para>
    /// </summary>
    private IReadOnlyList<ISearchValue> ConvertOrLog(
        IElementToSearchValueConverter converter,
        IElement extractedValue,
        string fhirPathExpression,
        string searchParameterDefinitionUrl,
        string resourceIdentity)
    {
        try
        {
            IEnumerable<ISearchValue> converted = converter.ConvertTo(extractedValue);

            // Already-materialized results are passed straight through: the base converter now returns a
            // List, and copying it again would cost an allocation per indexed element on the write path.
            // Anything still lazy is materialized here, inside the catch, which is the point of this method.
            return converted as IReadOnlyList<ISearchValue> ?? converted?.ToList() ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsExpectedEvaluationFailure(ex))
        {
            Log.ConverterFailed(_logger, ex, converter.GetType().Name, fhirPathExpression, searchParameterDefinitionUrl, resourceIdentity);
            return [];
        }
        catch (Exception ex)
        {
            // Unlike a bad literal or an unimplemented FHIRPath function, a NullReferenceException or
            // InvalidCastException reaching here means the converter itself is broken - it is a code
            // defect, not a data-quality problem. Failing the whole write over it would still be worse
            // than the one missing search parameter (see the "Materialized inside the try" containment
            // rationale on ExtractCompositeComponentSearchValues and ExtractSearchValues above), so this
            // stays contained. But it must not be logged identically to an expected miss: Error, not
            // Warning, so it surfaces to whatever is watching Error-level logs instead of blending into
            // routine "this literal didn't parse" noise.
            Log.UnexpectedConverterFailure(_logger, ex, converter.GetType().Name, fhirPathExpression, searchParameterDefinitionUrl, resourceIdentity);
            return [];
        }
    }

    /// <summary>
    /// True for FHIRPath evaluation failures the write path is expected to see against real-world data
    /// or custom search parameters: a bad literal, an unsupported/not-yet-implemented function, or any
    /// other expression-level rejection defined by <see cref="FhirPathEvaluationException"/>. Also true
    /// for the exception types a bad literal or a malformed custom expression actually surfaces as
    /// today: <see cref="FormatException"/> (e.g. <c>PartialDateTime.Parse</c> on a <c>Timing.event</c>
    /// that fails, or <c>FhirPathParser</c> failing to tokenize/parse an expression), <see cref="ArgumentException"/>
    /// (e.g. an empty custom search parameter expression), and <see cref="OverflowException"/> (a numeric
    /// literal outside the target type's range). False for anything else - a <see cref="NullReferenceException"/>
    /// or <see cref="InvalidCastException"/> reaching an indexing catch block means the indexer or a converter
    /// has a bug, not that the data or the expression was bad, and must not be logged the same way.
    /// </summary>
    /// <remarks>
    /// <see cref="ArgumentNullException"/> and <see cref="ArgumentOutOfRangeException"/> are excluded even
    /// though both derive from <see cref="ArgumentException"/>, because in this codebase they carry the
    /// opposite meaning. <c>FhirElementToSearchValueConverter&lt;T&gt;.ConvertTo</c> throws
    /// <see cref="ArgumentOutOfRangeException"/> when the converter manager hands it an element whose
    /// <c>InstanceType</c> it does not declare, and <c>NumberSearchValue</c>/<c>QuantitySearchValue</c>
    /// throw <see cref="ArgumentNullException"/> when constructed with no bounds at all. Both are dispatch
    /// or construction defects, which is exactly what the Error tier exists to surface. Matching the base
    /// type swept them into the Warning tier alongside a malformed patient date, inverting the distinction
    /// this predicate was added to draw.
    /// </remarks>
    private static bool IsExpectedEvaluationFailure(Exception ex) =>
        ex is FhirPathEvaluationException or NotSupportedException or FormatException or OverflowException
        || (ex is ArgumentException and not (ArgumentNullException or ArgumentOutOfRangeException));

    /// <summary>
    /// Builds the "ResourceType/id" label the indexing warnings are tagged with.
    /// <para>
    /// Read through <see cref="IElement.Children"/> rather than a FHIRPath expression on purpose: this label
    /// exists to describe evaluation failures, so resolving it must not re-enter the evaluator that just
    /// failed, and must not become a new way for indexing to throw.
    /// </para>
    /// </summary>
    private static string DescribeResource(IElement resource)
    {
        IReadOnlyList<IElement> idElements = resource.Children("id");
        string id = idElements.Count > 0 ? idElements[0]?.Value?.ToString() : null;

        return string.IsNullOrEmpty(id) ? $"{resource.InstanceType}/(no id)" : $"{resource.InstanceType}/{id}";
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
                return typeof(CompositeIndexSearchValue);
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

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to extract the values using '{FhirPathExpression}' against '{ElementType}' for search parameter '{SearchParameterUrl}' on resource '{ResourceIdentity}'.")]
        public static partial void FailedToExtractValues(ILogger logger, Exception ex, string fhirPathExpression, string elementType, string searchParameterUrl, string resourceIdentity);

        [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error extracting values using '{FhirPathExpression}' against '{ElementType}' for search parameter '{SearchParameterUrl}' on resource '{ResourceIdentity}'. This is not an expected FHIRPath evaluation failure and likely indicates a bug in the indexer or evaluator.")]
        public static partial void UnexpectedExtractionFailure(ILogger logger, Exception ex, string fhirPathExpression, string elementType, string searchParameterUrl, string resourceIdentity);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping element with null or empty InstanceType for search parameter '{SearchParameterUrl}' on resource '{ResourceIdentity}' during search indexing.")]
        public static partial void SkippingElementNullOrEmptyInstanceType(ILogger logger, string searchParameterUrl, string resourceIdentity);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Converter '{ConverterType}' failed on a value extracted by '{FhirPathExpression}' for search parameter '{SearchParameterUrl}' on resource '{ResourceIdentity}'. Skipping this value.")]
        public static partial void ConverterFailed(ILogger logger, Exception ex, string converterType, string fhirPathExpression, string searchParameterUrl, string resourceIdentity);

        [LoggerMessage(Level = LogLevel.Error, Message = "Converter '{ConverterType}' raised an unexpected error on a value extracted by '{FhirPathExpression}' for search parameter '{SearchParameterUrl}' on resource '{ResourceIdentity}'. This is not an expected evaluation failure and likely indicates a defect in the converter. Skipping this value.")]
        public static partial void UnexpectedConverterFailure(ILogger logger, Exception ex, string converterType, string fhirPathExpression, string searchParameterUrl, string resourceIdentity);

        [LoggerMessage(Level = LogLevel.Warning, Message = "The FHIR element '{ElementType}' is not supported for search parameter '{SearchParameterUrl}'.")]
        public static partial void FhirElementTypeNotSupported(ILogger logger, string elementType, string searchParameterUrl);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot infer SearchParamType from FHIR element type '{FhirElementType}' for composite component of search parameter '{SearchParameterUrl}'. Skipping this value.")]
        public static partial void CannotInferSearchParamType(ILogger logger, string fhirElementType, string searchParameterUrl);
    }
}
