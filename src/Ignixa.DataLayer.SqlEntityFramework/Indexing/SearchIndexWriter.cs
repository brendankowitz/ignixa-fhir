// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.DataLayer.SqlEntityFramework.Indexing;

/// <summary>
/// Writes search parameter indices to the database.
/// Converts ISearchValue objects to entity models and persists them.
/// </summary>
public class SearchIndexWriter
{
    private readonly FhirDbContext _context;
    private readonly ILogger<SearchIndexWriter> _logger;
    private readonly SearchIndexReferenceDataCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchIndexWriter"/> class.
    /// </summary>
    /// <param name="context">The EF Core DbContext.</param>
    /// <param name="cache">The reference data cache for lookup IDs.</param>
    /// <param name="logger">Logger instance.</param>
    public SearchIndexWriter(
        FhirDbContext context,
        SearchIndexReferenceDataCache cache,
        ILogger<SearchIndexWriter> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Writes search indices for a resource to the database.
    /// </summary>
    /// <param name="resourceTypeId">The resource type identifier.</param>
    /// <param name="resourceSurrogateId">The resource surrogate identifier.</param>
    /// <param name="searchIndices">The search indices extracted from the resource.</param>
    /// <param name="isHistory">Indicates if this is a historical version.</param>
    public async Task WriteSearchIndicesAsync(
        short resourceTypeId,
        long resourceSurrogateId,
        IReadOnlyList<object> searchIndices,
        bool isHistory)
    {
        ArgumentNullException.ThrowIfNull(searchIndices);

        _logger.LogDebug("Writing {Count} search indices for resource {ResourceSurrogateId}", searchIndices.Count, resourceSurrogateId);

        foreach (var index in searchIndices)
        {
            // Try to cast to SearchIndexEntry first
            if (index is SearchIndexEntry entry)
            {
                // Get search parameter ID from URI
                var searchParamId = await _cache.GetSearchParamIdAsync(entry.SearchParameter.Url?.ToString() ?? string.Empty);
                if (!searchParamId.HasValue)
                {
                    _logger.LogWarning("Search parameter not found for URI: {Uri}, skipping index", entry.SearchParameter.Url);
                    continue;
                }

                // Dispatch based on value type
                switch (entry.Value)
                {
                    case StringSearchValue stringValue:
                        await WriteStringSearchParamAsync(resourceTypeId, resourceSurrogateId, stringValue, searchParamId.Value, isHistory);
                        break;

                    case TokenSearchValue tokenValue:
                        await WriteTokenSearchParamAsync(resourceTypeId, resourceSurrogateId, tokenValue, searchParamId.Value, isHistory);
                        break;

                    case NumberSearchValue numberValue:
                        await WriteNumberSearchParamAsync(resourceTypeId, resourceSurrogateId, numberValue, searchParamId.Value, isHistory);
                        break;

                    case DateTimeSearchValue dateTimeValue:
                        await WriteDateTimeSearchParamAsync(resourceTypeId, resourceSurrogateId, dateTimeValue, searchParamId.Value, isHistory);
                        break;

                    case QuantitySearchValue quantityValue:
                        await WriteQuantitySearchParamAsync(resourceTypeId, resourceSurrogateId, quantityValue, searchParamId.Value, isHistory);
                        break;

                    case ReferenceSearchValue referenceValue:
                        await WriteReferenceSearchParamAsync(resourceTypeId, resourceSurrogateId, referenceValue, searchParamId.Value, isHistory);
                        break;

                    case UriSearchValue uriValue:
                        await WriteUriSearchParamAsync(resourceTypeId, resourceSurrogateId, uriValue, searchParamId.Value, isHistory);
                        break;

                    default:
                        _logger.LogWarning("Unsupported search value type: {Type}", entry.Value.GetType().Name);
                        break;
                }
            }
            else
            {
                // Fallback for raw ISearchValue objects (legacy support)
                _logger.LogWarning("Received raw search value without SearchIndexEntry wrapper, using placeholder ID 0");
                switch (index)
                {
                    case StringSearchValue stringValue:
                        await WriteStringSearchParamAsync(resourceTypeId, resourceSurrogateId, stringValue, 0, isHistory);
                        break;

                    case TokenSearchValue tokenValue:
                        await WriteTokenSearchParamAsync(resourceTypeId, resourceSurrogateId, tokenValue, 0, isHistory);
                        break;

                    case NumberSearchValue numberValue:
                        await WriteNumberSearchParamAsync(resourceTypeId, resourceSurrogateId, numberValue, 0, isHistory);
                        break;

                    case DateTimeSearchValue dateTimeValue:
                        await WriteDateTimeSearchParamAsync(resourceTypeId, resourceSurrogateId, dateTimeValue, 0, isHistory);
                        break;

                    case QuantitySearchValue quantityValue:
                        await WriteQuantitySearchParamAsync(resourceTypeId, resourceSurrogateId, quantityValue, 0, isHistory);
                        break;

                    case ReferenceSearchValue referenceValue:
                        await WriteReferenceSearchParamAsync(resourceTypeId, resourceSurrogateId, referenceValue, 0, isHistory);
                        break;

                    case UriSearchValue uriValue:
                        await WriteUriSearchParamAsync(resourceTypeId, resourceSurrogateId, uriValue, 0, isHistory);
                        break;

                    default:
                        _logger.LogWarning("Unsupported search value type: {Type}", index.GetType().Name);
                        break;
                }
            }
        }
    }

    private async Task WriteStringSearchParamAsync(
        short resourceTypeId,
        long resourceSurrogateId,
        StringSearchValue value,
        short searchParamId,
        bool isHistory)
    {

        var text = value.String;
        string? textOverflow = null;

        // If text is longer than 256 characters, split into Text and TextOverflow
        if (text.Length > 256)
        {
            textOverflow = text;
            text = text.Substring(0, 256);
        }

        // Check if an entity with the same composite key is already being tracked
        // Composite key: {ResourceTypeId, ResourceSurrogateId, SearchParamId, Text}
        var existingEntity = _context.StringSearchParams.Local
            .FirstOrDefault(e =>
                e.ResourceTypeId == resourceTypeId &&
                e.ResourceSurrogateId == resourceSurrogateId &&
                e.SearchParamId == searchParamId &&
                e.Text == text);

        if (existingEntity != null)
        {
            // Entity already tracked - skip duplicate
            _logger.LogDebug(
                "Skipping duplicate StringSearchParam: ResourceSurrogateId={ResourceSurrogateId}, SearchParamId={SearchParamId}, Text={Text}",
                resourceSurrogateId,
                searchParamId,
                text.Length > 50 ? string.Concat(text.AsSpan(0, 50), "...") : text);
            await Task.CompletedTask;
            return;
        }

        var entity = new StringSearchParamEntity
        {
            ResourceTypeId = resourceTypeId,
            ResourceSurrogateId = resourceSurrogateId,
            SearchParamId = searchParamId,
            Text = text,
            TextOverflow = textOverflow,
            IsMin = value.IsMin,
            IsMax = value.IsMax,
        };

        _context.StringSearchParams.Add(entity);
        await Task.CompletedTask; // Placeholder for future async operations
    }

    private async Task WriteTokenSearchParamAsync(
        short resourceTypeId,
        long resourceSurrogateId,
        TokenSearchValue value,
        short searchParamId,
        bool isHistory)
    {

        // Get or create SystemId
        var systemId = await _cache.GetOrCreateSystemIdAsync(value.System);

        var code = value.Code ?? string.Empty;
        string? codeOverflow = null;

        // If code is longer than 256 characters, split into Code and CodeOverflow
        if (code.Length > 256)
        {
            codeOverflow = code;
            code = code.Substring(0, 256);
        }

        // Check if an entity with the same composite key is already being tracked
        // Composite key: {ResourceTypeId, ResourceSurrogateId, SearchParamId, Code}
        var existingEntity = _context.TokenSearchParams.Local
            .FirstOrDefault(e =>
                e.ResourceTypeId == resourceTypeId &&
                e.ResourceSurrogateId == resourceSurrogateId &&
                e.SearchParamId == searchParamId &&
                e.Code == code);

        if (existingEntity != null)
        {
            // Entity already tracked - skip duplicate
            _logger.LogDebug(
                "Skipping duplicate TokenSearchParam: ResourceSurrogateId={ResourceSurrogateId}, SearchParamId={SearchParamId}, Code={Code}",
                resourceSurrogateId,
                searchParamId,
                code.Length > 50 ? string.Concat(code.AsSpan(0, 50), "...") : code);
            return;
        }

        var entity = new TokenSearchParamEntity
        {
            ResourceTypeId = resourceTypeId,
            ResourceSurrogateId = resourceSurrogateId,
            SearchParamId = searchParamId,
            SystemId = systemId,
            Code = code,
            CodeOverflow = codeOverflow,
        };

        _context.TokenSearchParams.Add(entity);
    }

    private async Task WriteNumberSearchParamAsync(
        short resourceTypeId,
        long resourceSurrogateId,
        NumberSearchValue value,
        short searchParamId,
        bool isHistory)
    {

        decimal? singleValue = null;
        if (value.Low == value.High)
        {
            singleValue = value.Low;
        }

        var entity = new NumberSearchParamEntity
        {
            ResourceTypeId = resourceTypeId,
            ResourceSurrogateId = resourceSurrogateId,
            SearchParamId = searchParamId,
            SingleValue = singleValue,
            LowValue = value.Low ?? 0,
            HighValue = value.High ?? 0,
        };

        _context.NumberSearchParams.Add(entity);
        await Task.CompletedTask; // Placeholder for future async operations
    }

    private async Task WriteDateTimeSearchParamAsync(
        short resourceTypeId,
        long resourceSurrogateId,
        DateTimeSearchValue value,
        short searchParamId,
        bool isHistory)
    {

        // Convert DateTimeOffset to DateTime (SQL Server datetime2)
        var startDateTime = value.Start.UtcDateTime;
        var endDateTime = value.End.UtcDateTime;

        // Check if longer than a day
        var isLongerThanADay = (endDateTime - startDateTime).TotalDays > 1;

        // Check if an entity with the same composite key is already being tracked
        // Composite key: {ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime}
        var existingEntity = _context.DateTimeSearchParams.Local
            .FirstOrDefault(e =>
                e.ResourceTypeId == resourceTypeId &&
                e.ResourceSurrogateId == resourceSurrogateId &&
                e.SearchParamId == searchParamId &&
                e.StartDateTime == startDateTime);

        if (existingEntity != null)
        {
            // Entity already tracked - skip duplicate
            _logger.LogDebug(
                "Skipping duplicate DateTimeSearchParam: ResourceSurrogateId={ResourceSurrogateId}, SearchParamId={SearchParamId}, StartDateTime={StartDateTime}",
                resourceSurrogateId,
                searchParamId,
                startDateTime);
            await Task.CompletedTask;
            return;
        }

        var entity = new DateTimeSearchParamEntity
        {
            ResourceTypeId = resourceTypeId,
            ResourceSurrogateId = resourceSurrogateId,
            SearchParamId = searchParamId,
            StartDateTime = startDateTime,
            EndDateTime = endDateTime,
            IsLongerThanADay = isLongerThanADay,
            IsMin = value.IsMin,
            IsMax = value.IsMax,
        };

        _context.DateTimeSearchParams.Add(entity);
        await Task.CompletedTask; // Placeholder for future async operations
    }

    private async Task WriteQuantitySearchParamAsync(
        short resourceTypeId,
        long resourceSurrogateId,
        QuantitySearchValue value,
        short searchParamId,
        bool isHistory)
    {

        // Get or create SystemId and QuantityCodeId
        var systemId = await _cache.GetOrCreateSystemIdAsync(value.System);
        var quantityCodeId = await _cache.GetOrCreateQuantityCodeIdAsync(value.Code);

        decimal? singleValue = null;
        if (value.Low == value.High)
        {
            singleValue = value.Low;
        }

        var entity = new QuantitySearchParamEntity
        {
            ResourceTypeId = resourceTypeId,
            ResourceSurrogateId = resourceSurrogateId,
            SearchParamId = searchParamId,
            SystemId = systemId,
            QuantityCodeId = quantityCodeId,
            SingleValue = singleValue,
            LowValue = value.Low ?? 0,
            HighValue = value.High ?? 0,
        };

        _context.QuantitySearchParams.Add(entity);
    }

    private async Task WriteReferenceSearchParamAsync(
        short resourceTypeId,
        long resourceSurrogateId,
        ReferenceSearchValue value,
        short searchParamId,
        bool isHistory)
    {

        // Get ResourceTypeId for the referenced resource
        var referenceResourceTypeId = await _cache.GetResourceTypeIdAsync(value.ResourceType);

        // Parse version from reference if present (format: "ResourceType/id/_history/version")
        int? referenceResourceVersion = null;
        if (!string.IsNullOrEmpty(value.ResourceId) && value.ResourceId.Contains("/_history/", StringComparison.Ordinal))
        {
            var parts = value.ResourceId.Split("/_history/");
            if (parts.Length == 2 && int.TryParse(parts[1], out var version))
            {
                referenceResourceVersion = version;
            }
        }

        // Check if an entity with the same composite key is already being tracked
        // Composite key: {ResourceTypeId, ResourceSurrogateId, SearchParamId, ReferenceResourceId}
        var existingEntity = _context.ReferenceSearchParams.Local
            .FirstOrDefault(e =>
                e.ResourceTypeId == resourceTypeId &&
                e.ResourceSurrogateId == resourceSurrogateId &&
                e.SearchParamId == searchParamId &&
                e.ReferenceResourceId == value.ResourceId);

        if (existingEntity != null)
        {
            // Entity already tracked - skip duplicate
            _logger.LogDebug(
                "Skipping duplicate ReferenceSearchParam: ResourceSurrogateId={ResourceSurrogateId}, SearchParamId={SearchParamId}, ReferenceResourceId={ReferenceResourceId}",
                resourceSurrogateId,
                searchParamId,
                value.ResourceId);
            return;
        }

        var entity = new ReferenceSearchParamEntity
        {
            ResourceTypeId = resourceTypeId,
            ResourceSurrogateId = resourceSurrogateId,
            SearchParamId = searchParamId,
            BaseUri = value.BaseUri?.ToString(),
            ReferenceResourceTypeId = referenceResourceTypeId,
            ReferenceResourceId = value.ResourceId,
            ReferenceResourceVersion = referenceResourceVersion,
        };

        _context.ReferenceSearchParams.Add(entity);
    }

    private async Task WriteUriSearchParamAsync(
        short resourceTypeId,
        long resourceSurrogateId,
        UriSearchValue value,
        short searchParamId,
        bool isHistory)
    {

        var uri = value.Uri;
        if (uri.Length > 256)
        {
            _logger.LogWarning("URI search value exceeds 256 characters, truncating: {Uri}", uri);
            uri = uri.Substring(0, 256);
        }

        // Check if an entity with the same composite key is already being tracked
        // Composite key: {ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri}
        var existingEntity = _context.UriSearchParams.Local
            .FirstOrDefault(e =>
                e.ResourceTypeId == resourceTypeId &&
                e.ResourceSurrogateId == resourceSurrogateId &&
                e.SearchParamId == searchParamId &&
                e.Uri == uri);

        if (existingEntity != null)
        {
            // Entity already tracked - skip duplicate
            _logger.LogDebug(
                "Skipping duplicate UriSearchParam: ResourceSurrogateId={ResourceSurrogateId}, SearchParamId={SearchParamId}, Uri={Uri}",
                resourceSurrogateId,
                searchParamId,
                uri.Length > 50 ? string.Concat(uri.AsSpan(0, 50), "...") : uri);
            await Task.CompletedTask;
            return;
        }

        var entity = new UriSearchParamEntity
        {
            ResourceTypeId = resourceTypeId,
            ResourceSurrogateId = resourceSurrogateId,
            SearchParamId = searchParamId,
            Uri = uri,
        };

        _context.UriSearchParams.Add(entity);
        await Task.CompletedTask; // Placeholder for future async operations
    }
}
