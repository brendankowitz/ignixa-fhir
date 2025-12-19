# ADR 2504: Phase 1.3 - Search Parameter Types

## Status

Proposed

## Context

Phase 1.2 established the InMemory search architecture with basic string search. Phase 1.3 extends this to support all fundamental FHIR search parameter types: Token, Date, Number, and Reference, plus the special search parameters (_id, _lastUpdated, _type).

### FHIR Search Parameter Types

| Type | Example | Use Case |
|------|---------|----------|
| **String** | `?name=John` | Human-readable text (names, addresses) |
| **Token** | `?gender=male` | Coded values (code, identifier, boolean) |
| **Date** | `?birthdate=ge1980-01-01` | Dates and datetimes with ranges |
| **Number** | `?value=gt100` | Numeric values with comparators |
| **Reference** | `?patient=Patient/123` | References to other resources |

**Special Parameters** (apply to all resources):
- `_id`: Resource ID
- `_lastUpdated`: Last modification timestamp
- `_type`: Resource type (for multi-type searches)

### Search Modifiers and Prefixes

**String Modifiers**:
- (none): Starts with (default)
- `:exact`: Exact match (case-sensitive)
- `:contains`: Contains substring

**Token Modifiers**:
- (none): Code match
- `:text`: Text match
- `:not`: Negation

**Date/Number Prefixes**:
- `eq`: Equal (default)
- `gt`: Greater than
- `ge`: Greater than or equal
- `lt`: Less than
- `le`: Less than or equal
- `ne`: Not equal

## Decision

Extend InMemory search to support:
1. **Token search** (coded values, identifiers, booleans)
2. **Date search** (with range support and partial dates)
3. **Number search** (with numeric comparisons)
4. **Reference search** (resource references)
5. **Special parameters** (_id, _lastUpdated, _type)
6. **Pagination** (continuation tokens)

### Architecture

#### 1. Search Parameter Type Implementations

**File Structure**:
```
Ignixa.Api/
  Features/
    Search/
      InMemory/
        Types/
          StringSearchHandler.cs     # Phase 1.2 (existing)
          TokenSearchHandler.cs      # NEW: Phase 1.3
          DateSearchHandler.cs       # NEW: Phase 1.3
          NumberSearchHandler.cs     # NEW: Phase 1.3
          ReferenceSearchHandler.cs  # NEW: Phase 1.3
        SpecialParameters/
          IdSearchHandler.cs         # NEW: _id
          LastUpdatedSearchHandler.cs # NEW: _lastUpdated
          TypeSearchHandler.cs       # NEW: _type
```

#### 2. Token Search Implementation

**Token Format**: `system|code` or just `code`

```csharp
// Ignixa.Api/Features/Search/InMemory/Types/TokenSearchHandler.cs
namespace Ignixa.Features.Search.InMemory.Types;

public class TokenSearchHandler : ISearchParameterHandler
{
    private readonly InMemoryIndex _index;

    public SearchParamType Type => SearchParamType.Token;

    public IEnumerable<string> Search(
        string resourceType,
        SearchParameterInfo param,
        string value,
        SearchModifierCode? modifier)
    {
        // Parse token value: system|code or code
        var (system, code) = ParseToken(value);

        return modifier switch
        {
            SearchModifierCode.Text => SearchTokenText(resourceType, param.Code, value),
            SearchModifierCode.Not => SearchTokenNot(resourceType, param.Code, system, code),
            _ => _index.SearchToken(resourceType, param.Code, system, code)
        };
    }

    private (string? System, string Code) ParseToken(string value)
    {
        var parts = value.Split('|', 2);
        return parts.Length == 2
            ? (parts[0], parts[1])   // system|code
            : (null, parts[0]);      // code only
    }

    private IEnumerable<string> SearchTokenText(
        string resourceType,
        string paramName,
        string text)
    {
        // Search display text (stored in special text index)
        return _index.SearchTokenText(resourceType, paramName, text);
    }

    private IEnumerable<string> SearchTokenNot(
        string resourceType,
        string paramName,
        string? system,
        string code)
    {
        // Get all resources of this type
        var allResources = _index.GetAllResourceIds(resourceType);

        // Subtract resources matching token
        var matching = _index.SearchToken(resourceType, paramName, system, code);

        return allResources.Except(matching);
    }
}
```

**Index Structure**:
```csharp
// InMemoryIndex.cs additions
public class InMemoryIndex
{
    // Key: ResourceType:ParamName:system|code -> List<ResourceId>
    private readonly ConcurrentDictionary<string, List<string>> _tokenIndex = new();

    // Key: ResourceType:ParamName:displayText -> List<ResourceId>
    private readonly ConcurrentDictionary<string, List<string>> _tokenTextIndex = new();

    public void AddTokenIndex(
        string resourceType,
        string resourceId,
        string paramName,
        string? system,
        string code,
        string? display = null)
    {
        // Add to token index
        var tokenKey = system != null
            ? $"{resourceType}:{paramName}:{system}|{code}"
            : $"{resourceType}:{paramName}:|{code}";

        _tokenIndex.AddOrUpdate(
            tokenKey,
            _ => new List<string> { resourceId },
            (_, list) => { list.Add(resourceId); return list; });

        // Add to text index (if display provided)
        if (display != null)
        {
            var textKey = $"{resourceType}:{paramName}:{NormalizeString(display)}";
            _tokenTextIndex.AddOrUpdate(
                textKey,
                _ => new List<string> { resourceId },
                (_, list) => { list.Add(resourceId); return list; });
        }
    }

    public IEnumerable<string> SearchTokenText(
        string resourceType,
        string paramName,
        string text)
    {
        var normalized = NormalizeString(text);
        var keyPrefix = $"{resourceType}:{paramName}:";

        return _tokenTextIndex
            .Where(kvp => kvp.Key.StartsWith(keyPrefix) &&
                         kvp.Key.Substring(keyPrefix.Length).Contains(normalized))
            .SelectMany(kvp => kvp.Value)
            .Distinct();
    }
}
```

**Example**:
```http
GET /Patient?gender=male
GET /Patient?gender=http://hl7.org/fhir/administrative-gender|male
GET /Patient?gender:text=Male
GET /Patient?gender:not=female
```

#### 3. Date Search Implementation

**Date Handling**:
- Support partial dates: `2020`, `2020-01`, `2020-01-15`
- Support date ranges: `ge2020-01-01`, `lt2021-01-01`
- Handle implicit ranges for partial dates

```csharp
// Ignixa.Api/Features/Search/InMemory/Types/DateSearchHandler.cs
namespace Ignixa.Features.Search.InMemory.Types;

public class DateSearchHandler : ISearchParameterHandler
{
    private readonly InMemoryIndex _index;

    public SearchParamType Type => SearchParamType.Date;

    public IEnumerable<string> Search(
        string resourceType,
        SearchParameterInfo param,
        string value,
        SearchModifierCode? modifier)
    {
        // Parse prefix and date
        var (prefix, dateValue) = ParseDatePrefix(value);

        // Parse date (handles partial dates)
        var (start, end) = ParseDateRange(dateValue);

        return prefix switch
        {
            SearchComparator.Eq => SearchDateEquals(resourceType, param.Code, start, end),
            SearchComparator.Gt => SearchDateGreaterThan(resourceType, param.Code, end),
            SearchComparator.Ge => SearchDateGreaterOrEqual(resourceType, param.Code, start),
            SearchComparator.Lt => SearchDateLessThan(resourceType, param.Code, start),
            SearchComparator.Le => SearchDateLessOrEqual(resourceType, param.Code, end),
            SearchComparator.Ne => SearchDateNotEquals(resourceType, param.Code, start, end),
            _ => SearchDateEquals(resourceType, param.Code, start, end)
        };
    }

    private (SearchComparator Prefix, string Date) ParseDatePrefix(string value)
    {
        if (value.StartsWith("eq"))
            return (SearchComparator.Eq, value.Substring(2));
        if (value.StartsWith("ge"))
            return (SearchComparator.Ge, value.Substring(2));
        if (value.StartsWith("gt"))
            return (SearchComparator.Gt, value.Substring(2));
        if (value.StartsWith("le"))
            return (SearchComparator.Le, value.Substring(2));
        if (value.StartsWith("lt"))
            return (SearchComparator.Lt, value.Substring(2));
        if (value.StartsWith("ne"))
            return (SearchComparator.Ne, value.Substring(2));

        return (SearchComparator.Eq, value);  // Default: equals
    }

    private (DateTimeOffset Start, DateTimeOffset End) ParseDateRange(string dateString)
    {
        // Handle partial dates by creating implicit range
        return dateString.Length switch
        {
            4 => ParseYear(dateString),      // "2020" -> 2020-01-01 to 2020-12-31
            7 => ParseYearMonth(dateString), // "2020-01" -> 2020-01-01 to 2020-01-31
            10 => ParseYearMonthDay(dateString), // "2020-01-15" -> full day range
            _ => ParseDateTime(dateString)   // Full datetime
        };
    }

    private (DateTimeOffset, DateTimeOffset) ParseYear(string year)
    {
        var y = int.Parse(year);
        var start = new DateTimeOffset(y, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(y, 12, 31, 23, 59, 59, TimeSpan.Zero);
        return (start, end);
    }

    private IEnumerable<string> SearchDateEquals(
        string resourceType,
        string paramName,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        // Resource date range overlaps search range
        return _index.SearchDateRange(resourceType, paramName, start, end);
    }

    private IEnumerable<string> SearchDateGreaterThan(
        string resourceType,
        string paramName,
        DateTimeOffset date)
    {
        return _index.SearchDateAfter(resourceType, paramName, date, inclusive: false);
    }
}
```

**Index Structure**:
```csharp
// InMemoryIndex.cs additions
public class InMemoryIndex
{
    // Store date ranges for each resource
    // Key: ResourceType:ResourceId:ParamName -> (Start, End)
    private readonly ConcurrentDictionary<string, (DateTimeOffset Start, DateTimeOffset End)> _dateIndex = new();

    public void AddDateIndex(
        string resourceType,
        string resourceId,
        string paramName,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var key = $"{resourceType}:{resourceId}:{paramName}";
        _dateIndex[key] = (start, end);
    }

    public IEnumerable<string> SearchDateRange(
        string resourceType,
        string paramName,
        DateTimeOffset searchStart,
        DateTimeOffset searchEnd)
    {
        var prefix = $"{resourceType}:";

        return _dateIndex
            .Where(kvp =>
            {
                if (!kvp.Key.StartsWith(prefix) || !kvp.Key.EndsWith($":{paramName}"))
                    return false;

                var (resourceStart, resourceEnd) = kvp.Value;

                // Ranges overlap if:
                // resourceStart <= searchEnd && resourceEnd >= searchStart
                return resourceStart <= searchEnd && resourceEnd >= searchStart;
            })
            .Select(kvp =>
            {
                // Extract resourceId from key: ResourceType:ResourceId:ParamName
                var parts = kvp.Key.Split(':');
                return parts[1];
            });
    }
}
```

**Example**:
```http
GET /Patient?birthdate=1980-01-15
GET /Patient?birthdate=ge1980
GET /Patient?birthdate=gt1980-01-01
GET /Observation?date=2024-01
```

#### 4. Number Search Implementation

```csharp
// Ignixa.Api/Features/Search/InMemory/Types/NumberSearchHandler.cs
namespace Ignixa.Features.Search.InMemory.Types;

public class NumberSearchHandler : ISearchParameterHandler
{
    private readonly InMemoryIndex _index;

    public SearchParamType Type => SearchParamType.Number;

    public IEnumerable<string> Search(
        string resourceType,
        SearchParameterInfo param,
        string value,
        SearchModifierCode? modifier)
    {
        var (prefix, numValue) = ParseNumberPrefix(value);
        var number = decimal.Parse(numValue);

        return prefix switch
        {
            SearchComparator.Eq => _index.SearchNumber(resourceType, param.Code, n => n == number),
            SearchComparator.Gt => _index.SearchNumber(resourceType, param.Code, n => n > number),
            SearchComparator.Ge => _index.SearchNumber(resourceType, param.Code, n => n >= number),
            SearchComparator.Lt => _index.SearchNumber(resourceType, param.Code, n => n < number),
            SearchComparator.Le => _index.SearchNumber(resourceType, param.Code, n => n <= number),
            SearchComparator.Ne => _index.SearchNumber(resourceType, param.Code, n => n != number),
            _ => _index.SearchNumber(resourceType, param.Code, n => n == number)
        };
    }
}
```

**Index Structure**:
```csharp
// InMemoryIndex.cs additions
public class InMemoryIndex
{
    // Key: ResourceType:ResourceId:ParamName -> decimal value
    private readonly ConcurrentDictionary<string, decimal> _numberIndex = new();

    public IEnumerable<string> SearchNumber(
        string resourceType,
        string paramName,
        Func<decimal, bool> predicate)
    {
        var prefix = $"{resourceType}:";

        return _numberIndex
            .Where(kvp =>
                kvp.Key.StartsWith(prefix) &&
                kvp.Key.EndsWith($":{paramName}") &&
                predicate(kvp.Value))
            .Select(kvp =>
            {
                var parts = kvp.Key.Split(':');
                return parts[1];  // ResourceId
            });
    }
}
```

**Example**:
```http
GET /Observation?value=100
GET /Observation?value=gt100
GET /Observation?value=ge50
GET /RiskAssessment?probability=lt0.5
```

#### 5. Reference Search Implementation

```csharp
// Ignixa.Api/Features/Search/InMemory/Types/ReferenceSearchHandler.cs
namespace Ignixa.Features.Search.InMemory.Types;

public class ReferenceSearchHandler : ISearchParameterHandler
{
    private readonly InMemoryIndex _index;

    public SearchParamType Type => SearchParamType.Reference;

    public IEnumerable<string> Search(
        string resourceType,
        SearchParameterInfo param,
        string value,
        SearchModifierCode? modifier)
    {
        // Parse reference: Patient/123 or just 123
        var (refType, refId) = ParseReference(value);

        return _index.SearchReference(resourceType, param.Code, refType, refId);
    }

    private (string? Type, string Id) ParseReference(string reference)
    {
        var parts = reference.Split('/', 2);
        return parts.Length == 2
            ? (parts[0], parts[1])   // Patient/123
            : (null, parts[0]);      // 123
    }
}
```

**Index Structure**:
```csharp
// InMemoryIndex.cs additions
public class InMemoryIndex
{
    // Key: ResourceType:ParamName:RefType/RefId -> List<ResourceId>
    private readonly ConcurrentDictionary<string, List<string>> _referenceIndex = new();

    public void AddReferenceIndex(
        string resourceType,
        string resourceId,
        string paramName,
        string? refType,
        string refId)
    {
        var key = refType != null
            ? $"{resourceType}:{paramName}:{refType}/{refId}"
            : $"{resourceType}:{paramName}:/{refId}";

        _referenceIndex.AddOrUpdate(
            key,
            _ => new List<string> { resourceId },
            (_, list) => { list.Add(resourceId); return list; });
    }

    public IEnumerable<string> SearchReference(
        string resourceType,
        string paramName,
        string? refType,
        string refId)
    {
        var key = refType != null
            ? $"{resourceType}:{paramName}:{refType}/{refId}"
            : $"{resourceType}:{paramName}:/{refId}";

        return _referenceIndex.TryGetValue(key, out var resourceIds)
            ? resourceIds
            : Enumerable.Empty<string>();
    }
}
```

**Example**:
```http
GET /Observation?subject=Patient/123
GET /Observation?patient=123
GET /DiagnosticReport?result=Observation/456
```

#### 6. Special Parameters

**_id Implementation**:
```csharp
// Ignixa.Api/Features/Search/InMemory/SpecialParameters/IdSearchHandler.cs
public class IdSearchHandler : ISearchParameterHandler
{
    private readonly IFhirRepository _repository;

    public string ParameterName => "_id";

    public IEnumerable<string> Search(
        string resourceType,
        string value,
        SearchModifierCode? modifier)
    {
        // Split comma-separated IDs
        var ids = value.Split(',');

        // Return matching IDs (validate existence)
        return ids.Where(id => ResourceExists(resourceType, id));
    }

    private bool ResourceExists(string resourceType, string id)
    {
        var key = new ResourceKey(resourceType, id);
        var resource = _repository.GetAsync(key, CancellationToken.None)
            .GetAwaiter().GetResult();
        return resource != null && !resource.IsDeleted;
    }
}
```

**_lastUpdated Implementation**:
```csharp
// Ignixa.Api/Features/Search/InMemory/SpecialParameters/LastUpdatedSearchHandler.cs
public class LastUpdatedSearchHandler : ISearchParameterHandler
{
    private readonly InMemoryIndex _index;

    public string ParameterName => "_lastUpdated";

    public IEnumerable<string> Search(
        string resourceType,
        string value,
        SearchModifierCode? modifier)
    {
        // Reuse DateSearchHandler logic
        var dateHandler = new DateSearchHandler(_index);

        // _lastUpdated is indexed automatically for all resources
        return dateHandler.Search(resourceType,
            new SearchParameterInfo
            {
                Code = "_lastUpdated",
                Type = SearchParamType.Date
            },
            value,
            modifier);
    }
}
```

**Example**:
```http
GET /Patient?_id=123,456,789
GET /Patient?_lastUpdated=ge2024-01-01
GET /Observation?_lastUpdated=lt2024-10-08
```

#### 7. Pagination with Continuation Tokens

```csharp
// Ignixa.Api/Features/Search/Pagination/ContinuationTokenService.cs
namespace Ignixa.Features.Search.Pagination;

public class ContinuationTokenService
{
    public string CreateToken(PaginationContext context)
    {
        // Encode pagination state
        var state = new PaginationState
        {
            ResourceType = context.ResourceType,
            SearchParams = context.SearchParams,
            Offset = context.Offset,
            Count = context.Count
        };

        var json = JsonSerializer.Serialize(state);
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes);
    }

    public PaginationState? ParseToken(string token)
    {
        try
        {
            var bytes = Convert.FromBase64String(token);
            var json = Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<PaginationState>(json);
        }
        catch
        {
            return null;
        }
    }
}

public record PaginationState(
    string ResourceType,
    Dictionary<string, string> SearchParams,
    int Offset,
    int Count);

// In SearchHandler
public async ValueTask<SearchResult> SearchAsync(
    string resourceType,
    IReadOnlyDictionary<string, string> searchParams,
    CancellationToken ct)
{
    // Check for continuation token
    var offset = 0;
    if (searchParams.TryGetValue("_continuationToken", out var token))
    {
        var state = _continuationTokenService.ParseToken(token);
        offset = state?.Offset ?? 0;
    }

    // Get count parameter (default: 10, max: 100)
    var count = searchParams.TryGetValue("_count", out var countStr) &&
                int.TryParse(countStr, out var c)
        ? Math.Min(c, 100)
        : 10;

    // Execute search
    var allMatchingIds = ExecuteSearch(resourceType, searchParams);

    // Apply pagination
    var pagedIds = allMatchingIds.Skip(offset).Take(count).ToList();
    var resources = await LoadResourcesAsync(pagedIds, ct);

    // Create continuation token if more results
    string? nextToken = null;
    if (allMatchingIds.Count > offset + count)
    {
        nextToken = _continuationTokenService.CreateToken(new PaginationContext
        {
            ResourceType = resourceType,
            SearchParams = searchParams.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            Offset = offset + count,
            Count = count
        });
    }

    return new SearchResult(resources, nextToken, allMatchingIds.Count);
}
```

**Example**:
```http
GET /Patient?name=John&_count=10
→ Returns first 10, with continuationToken in Link header

GET /Patient?_continuationToken=eyJSZXNvdXJjZVR5cGUi...
→ Returns next 10
```

### Week 4 Implementation Plan (~16 Claude Code hours)

#### 1. Token Search (4 hours)

**Tasks**:
- Implement TokenSearchHandler
- Add token index to InMemoryIndex
- Support :text and :not modifiers
- Update IndexLoaderService to extract token values

**Testing**:
- Unit tests for TokenSearchHandler
- E2E: TokenSearchTests.cs (gender, identifier, boolean)

#### 2. Date Search (4 hours)

**Tasks**:
- Implement DateSearchHandler
- Add date range index to InMemoryIndex
- Support partial dates (year, year-month)
- Support all prefixes (eq, gt, ge, lt, le, ne)

**Testing**:
- Unit tests for DateSearchHandler
- E2E: DateSearchTests.cs (birthdate, date ranges)

#### 3. Number Search (2 hours)

**Tasks**:
- Implement NumberSearchHandler
- Add number index to InMemoryIndex
- Support numeric comparisons

**Testing**:
- Unit tests for NumberSearchHandler
- E2E: NumberSearchTests.cs (value, probability)

#### 4. Reference Search (2 hours)

**Tasks**:
- Implement ReferenceSearchHandler
- Add reference index to InMemoryIndex

**Testing**:
- Unit tests for ReferenceSearchHandler
- E2E: ReferenceSearchTests.cs (subject, patient, result)

#### 5. Special Parameters (2 hours)

**Tasks**:
- Implement IdSearchHandler
- Implement LastUpdatedSearchHandler
- Implement TypeSearchHandler

**Testing**:
- E2E: SpecialParameterTests.cs (_id, _lastUpdated, _type)

#### 6. Pagination (2 hours)

**Tasks**:
- Implement ContinuationTokenService
- Add pagination logic to SearchHandler
- Return Link headers per FHIR spec

**Testing**:
- E2E: PaginationTests.cs (continuation tokens, _count)

## Consequences

### Positive

1. **Complete Search Support**: All fundamental FHIR search types implemented
2. **FHIR Compliant**: Handles modifiers, prefixes, partial dates per spec
3. **Performance**: InMemory index provides fast lookups for all types
4. **Pagination**: Continuation tokens enable large result sets

### Negative

1. **Memory Footprint**: Multiple indices (string, token, date, number, reference) increase memory usage
2. **Index Consistency**: All indices must stay in sync with resource storage

### Risks

1. **Date Parsing Edge Cases**: Partial dates, timezones, implicit ranges are complex
2. **Pagination State**: Continuation tokens must handle concurrent updates

## References

- Investigation: `docs/investigations/phase1-file-based-storage-with-search.md`
- FHIR Search Specification: https://hl7.org/fhir/search.html
- ADR-2500: Master Implementation Roadmap
- ADR-2501: Prototype Phase
- ADR-2502: Phase 1.1 - Bundle Processing
- ADR-2503: Phase 1.2 - Search Implementation

## Next Steps

1. **Begin Phase 1.3 Implementation** (Week 4)
2. **Implement all search parameter types** (Token, Date, Number, Reference)
3. **Add special parameters** (_id, _lastUpdated, _type)
4. **Implement pagination** with continuation tokens
5. **Phase 2**: Extend to multi-resource CRUD (Observation, Encounter)
