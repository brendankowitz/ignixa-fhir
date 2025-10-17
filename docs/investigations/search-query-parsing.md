# FHIR Search Query Parsing Implementation

## Executive Summary

This document outlines the implementation strategy for parsing FHIR search query strings into expression trees using the newly organized `Ignixa.Search.Expressions` namespace. The approach simplifies the overly complex legacy `SearchOptionsFactory` while maintaining full FHIR R4 search compliance.

**Key Changes from Legacy**:
- ✅ Expressions moved to `Ignixa.Search.Expressions` (completed)
- ✅ Expression parser already available (`ExpressionParser.cs`)
- 🔲 Simplified `SearchOptionsBuilder` (to implement)
- 🔲 Modern query string parsing with `Span<T>` optimizations

---

## Legacy Implementation Analysis: SearchOptionsFactory

### Overview

The Microsoft FHIR Server's `SearchOptionsFactory` (~800 lines) is responsible for:
1. Parsing HTTP query parameters into `SearchOptions`
2. Building expression trees via `ExpressionParser`
3. Handling pagination, sorting, includes, and compartments
4. Validating parameters and enforcing limits
5. SMART on FHIR authorization filtering

### Pseudo-Logic Breakdown

```csharp
// SearchOptionsFactory.Create() - Line 88-592

public SearchOptions Create(
    string compartmentType,
    string compartmentId,
    string resourceType,
    IReadOnlyList<Tuple<string, string>> queryParameters)
{
    var searchOptions = new SearchOptions();
    var searchExpressions = new List<Expression>();
    var unsupportedParams = new List<Tuple<string, string>>();

    // STEP 1: Parse special control parameters (lines 135-291)
    foreach (var (key, value) in queryParameters)
    {
        switch (key)
        {
            case "ct": // continuation token
                searchOptions.ContinuationToken = Decode(value);
                break;

            case "_total":
                searchOptions.IncludeTotal = ParseTotalType(value);
                break;

            case "_count":
                if (value == "0")
                    searchOptions.CountOnly = true;
                else
                    searchOptions.MaxItemCount = Min(value, maxAllowed);
                break;

            case "_format":
            case "_pretty":
                // Ignore - not search parameters
                break;

            case "_type":
                // Multi-resource type search
                ParseResourceTypes(value);
                break;

            case "_summary":
                if (value == "count")
                    searchOptions.CountOnly = true;
                break;

            case "_elements":
                // Not a search param - handled elsewhere
                break;

            case "_include":
            case "_revinclude":
                // Handled later
                break;

            case "_sort":
                // Handled later
                break;

            default:
                // Regular search parameter
                searchParams.Add(key, value);
                break;
        }
    }

    // STEP 2: Determine resource types (lines 373-403)
    string[] resourceTypes;
    if (string.IsNullOrEmpty(resourceType))
    {
        // Extract from _type parameter or default to DomainResource
        resourceTypes = ExtractTypesFromTypeParameter() ?? ["DomainResource"];
    }
    else
    {
        resourceTypes = [resourceType];

        // Add _type filter to expression tree
        searchExpressions.Add(
            Expression.SearchParameter(
                ResourceTypeSearchParameter,
                Expression.StringEquals(FieldName.TokenCode, null, resourceType, false)
            )
        );
    }

    // STEP 3: Parse search parameters into expressions (lines 409-423)
    foreach (var (key, value) in searchParams.Parameters)
    {
        try
        {
            // THIS IS WHERE THE MAGIC HAPPENS
            Expression expr = _expressionParser.Parse(resourceTypes, key, value);
            searchExpressions.Add(expr);
        }
        catch (SearchParameterNotSupportedException)
        {
            unsupportedParams.Add((key, value));
        }
    }

    // STEP 4: Parse _include and _revinclude (lines 425-429)
    searchExpressions.AddRange(
        ParseIncludeExpressions(searchParams.Include, resourceTypes, isReversed: false)
    );
    searchExpressions.AddRange(
        ParseIncludeExpressions(searchParams.RevInclude, resourceTypes, isReversed: true)
    );

    // STEP 5: Add compartment filters (lines 431-479)
    if (!string.IsNullOrEmpty(compartmentType))
    {
        searchExpressions.Add(
            Expression.CompartmentSearch(compartmentType, compartmentId, resourceTypes)
        );
    }

    // STEP 6: Add SMART on FHIR filters (lines 455-479)
    if (HasSmartCompartment())
    {
        searchExpressions.Add(
            Expression.SmartCompartmentSearch(smartType, smartId, null)
        );
    }

    // STEP 7: Combine all expressions with AND (lines 486-493)
    if (searchExpressions.Count == 1)
        searchOptions.Expression = searchExpressions[0];
    else if (searchExpressions.Count > 1)
        searchOptions.Expression = Expression.And(searchExpressions.ToArray());

    // STEP 8: Parse sorting (lines 500-544)
    var sortings = new List<(SearchParameterInfo, SortOrder)>();
    foreach (var (param, order) in searchParams.Sort)
    {
        var searchParam = _searchParamDefinitionManager.GetSearchParameter(resourceType, param);
        sortings.Add((searchParam, order));
    }
    searchOptions.Sort = sortings;

    // STEP 9: Validate and report unsupported params (lines 555-578)
    if (unsupportedParams.Any())
    {
        if (strictHandling)
            throw new BadRequestException(unsupportedParams);
        else
            AddWarnings(unsupportedParams);
    }

    return searchOptions;
}
```

### Key Complexity Sources

| Component | Lines | Complexity Reason |
|-----------|-------|-------------------|
| **Parameter Loop** | ~150 | Handles 15+ special parameters with conditional logic |
| **Resource Type Resolution** | ~30 | Multi-resource searches, validation, _type parameter |
| **Include Parsing** | ~75 | Iterative includes, circular references, wildcards |
| **Compartment Logic** | ~50 | Regular + SMART compartments, authorization |
| **SMART Authorization** | ~50 | Fine-grained access control, clinical scopes |
| **Sorting Validation** | ~45 | Parameter validation, multi-resource compatibility |
| **Error Handling** | ~50 | Strict vs lenient handling, warnings, logging |

**Total**: ~800 lines, 40+ conditional branches

---

## Ignixa v2 Simplified Approach

### Design Principles

1. **Separation of Concerns**: Query parsing → Expression building → Options creation
2. **Progressive Enhancement**: Start simple, add features incrementally
3. **Testability**: Each stage independently testable
4. **Performance**: Span<T>, minimal allocations, Expression tree reuse

### Architecture: Three-Stage Pipeline

```
HTTP Query String
    ↓
┌────────────────────────────────────┐
│ 1. QueryParameterParser            │  Parse query string
│    - Extracts key-value pairs      │  into structured form
│    - Handles URL decoding          │
│    - Classifies parameter types    │
└────────────────────────────────────┘
    ↓ QueryParameters (structured)
┌────────────────────────────────────┐
│ 2. ExpressionBuilder               │  Build expression tree
│    - Uses ExpressionParser (exists)│  from search parameters
│    - Combines with AND/OR logic    │
│    - Adds compartment filters      │
└────────────────────────────────────┘
    ↓ Expression (tree)
┌────────────────────────────────────┐
│ 3. SearchOptionsBuilder            │  Create SearchOptions
│    - Sets pagination (count, ct)   │  with all settings
│    - Sets sorting                  │
│    - Sets includes                 │
│    - Validates limits              │
└────────────────────────────────────┘
    ↓ SearchOptions
```

---

## Phase 1.2 Implementation (Simplified)

### What We Already Have ✅

**From Ignixa.Search.Expressions** (relocated in this session):
- `Expression.cs` - Base class with factory methods
- `ExpressionParser.cs` - Parses query params into expressions
- `SearchParameterExpressionParser.cs` - Parses individual search parameters
- All expression types (Binary, String, Multiary, Chained, Include, etc.)

### What We Need to Build 🔲

#### 1. QueryParameterParser (New)

**Purpose**: Parse raw query string into structured parameters

```csharp
namespace Ignixa.Search.Parsing;

public record QueryParameter(string Name, string Value)
{
    public ParameterCategory Category => ClassifyParameter(Name);

    private static ParameterCategory ClassifyParameter(string name) => name switch
    {
        "ct" => ParameterCategory.ContinuationToken,
        "_count" => ParameterCategory.Count,
        "_total" => ParameterCategory.Total,
        "_summary" => ParameterCategory.Summary,
        "_sort" => ParameterCategory.Sort,
        "_include" => ParameterCategory.Include,
        "_revinclude" => ParameterCategory.RevInclude,
        "_elements" => ParameterCategory.Elements,
        "_format" or "_pretty" => ParameterCategory.Formatting,
        "_type" => ParameterCategory.Type,
        _ when name.StartsWith("_") => ParameterCategory.Control,
        _ => ParameterCategory.Search
    };
}

public enum ParameterCategory
{
    Search,           // name=John
    ContinuationToken,// ct=...
    Count,            // _count=10
    Total,            // _total=accurate
    Summary,          // _summary=count
    Sort,             // _sort=name,-birthdate
    Include,          // _include=Patient:general-practitioner
    RevInclude,       // _revinclude=Observation:patient
    Elements,         // _elements=id,name
    Type,             // _type=Patient,Observation
    Control,          // Other _* parameters
    Formatting        // _format, _pretty (ignore)
}

public class QueryParameterParser
{
    public IReadOnlyList<QueryParameter> Parse(IEnumerable<(string key, string value)> queryParams)
    {
        return queryParams
            .Where(p => !string.IsNullOrWhiteSpace(p.key) && !string.IsNullOrWhiteSpace(p.value))
            .Select(p => new QueryParameter(p.key, p.value))
            .ToList();
    }
}
```

**Complexity**: ~50 lines vs legacy's ~150 lines

---

#### 2. SearchOptionsBuilder (New - Simplified)

**Purpose**: Build `SearchOptions` from query parameters and expression tree

```csharp
namespace Ignixa.Search.Options;

public class SearchOptionsBuilder
{
    private readonly IExpressionParser _expressionParser;
    private readonly ISearchParameterDefinitionManager _searchParamManager;
    private readonly SearchConfiguration _config;

    public SearchOptions Build(
        string resourceType,
        IReadOnlyList<QueryParameter> parameters)
    {
        var options = new SearchOptions();
        var searchParams = new List<(string, string)>();
        var includes = new List<string>();
        var revIncludes = new List<string>();
        var sorts = new List<string>();

        // STEP 1: Categorize parameters
        foreach (var param in parameters)
        {
            switch (param.Category)
            {
                case ParameterCategory.ContinuationToken:
                    options.ContinuationToken = DecodeContinuationToken(param.Value);
                    break;

                case ParameterCategory.Count:
                    options.MaxItemCount = ParseCount(param.Value);
                    options.MaxItemCountSpecifiedByClient = true;
                    break;

                case ParameterCategory.Total:
                    options.IncludeTotal = ParseTotalType(param.Value);
                    break;

                case ParameterCategory.Summary:
                    if (param.Value.Equals("count", StringComparison.OrdinalIgnoreCase))
                        options.CountOnly = true;
                    break;

                case ParameterCategory.Sort:
                    sorts.AddRange(param.Value.Split(','));
                    break;

                case ParameterCategory.Include:
                    includes.Add(param.Value);
                    break;

                case ParameterCategory.RevInclude:
                    revIncludes.Add(param.Value);
                    break;

                case ParameterCategory.Search:
                    searchParams.Add((param.Name, param.Value));
                    break;

                case ParameterCategory.Formatting:
                    // Ignore - handled by formatter
                    break;
            }
        }

        // STEP 2: Build expression tree from search parameters
        var expressions = new List<Expression>();

        // Add resource type filter
        if (!string.IsNullOrEmpty(resourceType))
        {
            expressions.Add(CreateResourceTypeExpression(resourceType));
        }

        // Parse search parameters
        foreach (var (name, value) in searchParams)
        {
            Expression expr = _expressionParser.Parse([resourceType], name, value);
            expressions.Add(expr);
        }

        // Parse includes
        foreach (var include in includes)
        {
            var expr = _expressionParser.ParseInclude([resourceType], include, isReversed: false, iterate: false);
            expressions.Add(expr);
        }

        // Parse revIncludes
        foreach (var revInclude in revIncludes)
        {
            var expr = _expressionParser.ParseInclude([resourceType], revInclude, isReversed: true, iterate: false);
            expressions.Add(expr);
        }

        // STEP 3: Combine expressions
        options.Expression = expressions.Count switch
        {
            0 => null,
            1 => expressions[0],
            _ => Expression.And(expressions.ToArray())
        };

        // STEP 4: Parse sorting
        options.Sort = ParseSortParameters(resourceType, sorts);

        // STEP 5: Apply defaults and limits
        ApplyDefaults(options);
        EnforceLimits(options);

        return options;
    }

    private void ApplyDefaults(SearchOptions options)
    {
        if (options.MaxItemCount == 0)
            options.MaxItemCount = _config.DefaultPageSize; // 10

        if (options.IncludeTotal == TotalType.None && !options.CountOnly)
            options.IncludeTotal = _config.DefaultTotalType; // TotalType.None
    }

    private void EnforceLimits(SearchOptions options)
    {
        if (options.MaxItemCount > _config.MaxPageSize)
            options.MaxItemCount = _config.MaxPageSize; // 100
    }
}
```

**Complexity**: ~200 lines vs legacy's ~800 lines

---

#### 3. SearchOptions Model (Simplified)

```csharp
namespace Ignixa.Domain.Models;

public class SearchOptions
{
    public Expression? Expression { get; set; }

    public int MaxItemCount { get; set; } = 10;
    public bool MaxItemCountSpecifiedByClient { get; set; }

    public TotalType IncludeTotal { get; set; } = TotalType.None;

    public bool CountOnly { get; set; }

    public string? ContinuationToken { get; set; }

    public IReadOnlyList<(SearchParameterInfo Parameter, SortOrder Order)> Sort { get; set; }
        = Array.Empty<(SearchParameterInfo, SortOrder)>();

    public IReadOnlyList<(string Name, string Value)> UnsupportedParams { get; set; }
        = Array.Empty<(string, string)>();
}

public enum TotalType
{
    None,      // Don't include total
    Accurate   // Include accurate count
}

public enum SortOrder
{
    Ascending,
    Descending
}
```

---

## Usage Example

### Controller → SearchOptionsBuilder → Expression Tree

```csharp
// PatientController.cs

[HttpGet]
public async Task<IActionResult> Search(
    [FromQuery] string? name,
    [FromQuery] string? birthdate,
    [FromQuery] string? _count,
    [FromQuery] string? _sort,
    [FromQuery] string? ct,
    CancellationToken cancellationToken)
{
    // 1. Extract query parameters
    var queryParams = HttpContext.Request.Query
        .Select(kv => (kv.Key, kv.Value.ToString()))
        .ToList();

    // 2. Parse into structured form
    var parser = new QueryParameterParser();
    var parameters = parser.Parse(queryParams);

    // 3. Build search options with expression tree
    var builder = _serviceProvider.GetRequiredService<SearchOptionsBuilder>();
    SearchOptions options = builder.Build("Patient", parameters);

    // 4. Execute search using expression tree
    var query = new SearchResourcesQuery("Patient", options);
    SearchResult result = await _mediator.SendAsync(query, cancellationToken);

    // 5. Return bundle
    return Ok(result.ToBundle());
}
```

### Expression Tree Result

For query: `GET /Patient?name=John&birthdate=gt1980-01-01&_sort=name`

```csharp
// options.Expression:
MultiaryExpression(AND) {
    Expressions = [
        SearchParameterExpression(name) {
            Expression = StringExpression(StartsWith, "John", ignoreCase: true)
        },
        SearchParameterExpression(birthdate) {
            Expression = BinaryExpression(GreaterThan, DateTime(1980-01-01))
        }
    ]
}

// options.Sort:
[
    (SearchParameterInfo("name"), SortOrder.Ascending)
]

// options.MaxItemCount: 10 (default)
```

---

## Comparison: Legacy vs Ignixa v2

| Aspect | Legacy SearchOptionsFactory | Ignixa v2 SearchOptionsBuilder |
|--------|----------------------------|-------------------------------|
| **Lines of Code** | ~800 | ~250 (70% reduction) |
| **Dependencies** | 12 injected services | 3 injected services |
| **Conditional Branches** | 40+ if/switch statements | 15 switch cases |
| **SMART on FHIR** | Built-in (~100 lines) | Deferred to Phase 10 |
| **Compartments** | Built-in (~50 lines) | Deferred to Phase 10 |
| **Error Handling** | Strict/Lenient modes | Simplified (warnings only) |
| **Query Hints** | CosmosDB-specific | Not needed (file-based) |
| **Continuation Tokens** | Complex encoding | Simple base64 |
| **Performance** | String allocations | Span<T> where possible |

---

## Implementation Roadmap

### Week 1: Foundation
- [x] ~~Move expression classes to `Ignixa.Search.Expressions`~~ (COMPLETED THIS SESSION)
- [ ] Create `QueryParameter` model and parser
- [ ] Create `SearchOptions` model in `Ignixa.Domain`
- [ ] Unit tests for query parameter parsing

### Week 2: Core Builder
- [ ] Implement `SearchOptionsBuilder` (basic)
- [ ] Wire up `ExpressionParser` integration
- [ ] Handle pagination (_count, ct)
- [ ] Handle summary (_summary=count)
- [ ] Unit tests for builder logic

### Week 3: Advanced Features
- [ ] Implement sorting (_sort)
- [ ] Implement includes (_include, _revinclude)
- [ ] Handle _total parameter
- [ ] Implement _elements (if needed)
- [ ] Integration tests with PatientController

### Week 4: Polish
- [ ] Performance optimization (Span<T>, pooling)
- [ ] Error handling and validation
- [ ] Documentation
- [ ] End-to-end testing

---

## Deferred Features (Post-Phase 1.2)

**Deferred to Phase 10** (SMART on FHIR):
- Compartment search expressions
- SMART compartment filters
- Fine-grained access control
- Clinical scope filtering

**Deferred to Phase 9** (CosmosDB):
- Query hints (GlobalEndSurrogateId, etc.)
- Feed range support
- Partition-specific optimizations

**Deferred to Phase 3** (Multi-tenancy):
- Tenant-specific search parameters
- Tenant isolation in search

---

## Performance Targets

| Operation | Target | Notes |
|-----------|--------|-------|
| Query parameter parsing | <1ms | Span<T> optimizations |
| Expression tree building | <5ms | Reuse parser instances |
| End-to-end (parse → options) | <10ms | Including validation |
| Memory allocations | <10KB | Pool strings where possible |

---

## Testing Strategy

### Unit Tests

```csharp
public class SearchOptionsBuilderTests
{
    [Fact]
    public void Build_WithNameParameter_CreatesStringExpression()
    {
        // Arrange
        var params = new[] { new QueryParameter("name", "John") };

        // Act
        var options = _builder.Build("Patient", params);

        // Assert
        Assert.NotNull(options.Expression);
        var expr = Assert.IsType<SearchParameterExpression>(options.Expression);
        Assert.Equal("name", expr.Parameter.Code);
    }

    [Fact]
    public void Build_WithCountParameter_SetsMaxItemCount()
    {
        // Arrange
        var params = new[] { new QueryParameter("_count", "20") };

        // Act
        var options = _builder.Build("Patient", params);

        // Assert
        Assert.Equal(20, options.MaxItemCount);
        Assert.True(options.MaxItemCountSpecifiedByClient);
    }

    [Theory]
    [InlineData("name=John&birthdate=1980", 2)] // 2 search params
    [InlineData("name=John&_count=10", 1)]      // 1 search + 1 control
    [InlineData("_summary=count", 0)]            // Only control param
    public void Build_WithMultipleParameters_CombinesExpressions(string query, int expectedExprCount)
    {
        // Parse query, build options, assert expression count
    }
}
```

### Integration Tests

```csharp
public class SearchIntegrationTests : IClassFixture<FhirServerFixture>
{
    [Fact]
    public async Task GET_Patient_WithNameFilter_ReturnsMatchingPatients()
    {
        // Arrange
        await SeedPatients("John Doe", "Jane Smith");

        // Act
        var response = await _client.GetAsync("/Patient?name=John");

        // Assert
        var bundle = await response.Content.ReadFromJsonAsync<Bundle>();
        Assert.Single(bundle.Entry);
        Assert.Equal("John Doe", GetPatientName(bundle.Entry[0].Resource));
    }
}
```

---

## Next Steps

1. **Create `QueryParameterParser`** - Start with simple implementation
2. **Create `SearchOptions` model** - Move to `Ignixa.Domain`
3. **Create `SearchOptionsBuilder`** - Implement basic version
4. **Wire into PatientController** - Replace manual parsing
5. **Add tests** - Unit + integration coverage

**Ready to proceed?** The expression tree infrastructure is in place (`Ignixa.Search.Expressions`), we just need the query parameter parsing and options building logic.

---

## References

- **Legacy Code**: `src-old/Microsoft.Health.Fhir.Shared.Core/Features/Search/SearchOptionsFactory.cs`
- **Expression Classes**: `src/Ignixa.Search/Expressions/` (relocated this session)
- **Expression Parser**: `src/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs`
- **FHIR Search Spec**: https://hl7.org/fhir/R4/search.html
