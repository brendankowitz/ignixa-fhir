# Investigation: Parallel Search Indexing Feasibility Analysis

**Feature**: search
**Status**: Viable
**Created**: 2025-10-31
**Original ADR**: N/A

## Executive Summary

**Scope**: Evaluate feasibility of parallelizing FHIR search parameter extraction
**Current Performance**: 52.7 μs sequential (after 44x optimization from Phases 2 & 3)

✅ **Parallel search indexing IS FEASIBLE** with careful implementation
⚠️ **Limited performance gains expected** (2-3x at most on high-core systems)
📊 **Recommendation**: LOW PRIORITY - Focus on database I/O optimization first

### Key Findings

| Aspect | Assessment | Notes |
|--------|-----------|-------|
| **Thread-Safety** | ✅ Likely Safe | `ITypedElement` navigation appears read-only |
| **Dependencies** | ✅ Independent | Search parameters process independently |
| **GC Pressure** | ⚠️ Concern | More allocations from parallel overhead |
| **Performance Gain** | ⚠️ Limited | 2-3x max (CPU-bound with caching) |
| **Complexity** | ❌ High | Requires careful synchronization |

## Current Implementation Analysis

### Sequential Processing Loop

**Location**: `src/Ignixa.Search/Indexing/TypedElementSearchIndexer.cs:73-84`

```csharp
foreach (SearchParameterInfo searchParameter in searchParameters)
{
    // Skip resource-table parameters
    if (searchParameter.Code is "_id" or "_lastUpdated" or "_type")
        continue;

    if (searchParameter.Type == SearchParamType.Composite)
        entries.AddRange(ProcessCompositeSearchParameter(searchParameter, resource, context));
    else
        entries.AddRange(ProcessNonCompositeSearchParameter(searchParameter, resource, context));
}
```

**Characteristics**:
- ~100 search parameters per resource type (e.g., Patient has ~100)
- Each parameter processes independently (no shared state)
- Results collected into `List<SearchIndexEntry>` (not thread-safe)

### Performance Breakdown (Measured)

| Component | Time (μs) | % of Total | Notes |
|-----------|-----------|------------|-------|
| **Total Indexing** | 52.7 | 100% | Already optimized 44x |
| Parse JSON | 3.9 | 7.4% | Not part of indexing |
| Schema Navigation | 0.02 | 0.04% | Negligible |
| **FHIRPath Evaluation** | ~49 | 93% | Delegate-compiled (Phase 3) |

**Observation**: FHIRPath evaluation dominates, but it's already heavily optimized with delegate compilation (Phase 3, 7x speedup).

## Thread-Safety Analysis

### ITypedElement Navigation (`TypedElementOnSourceNode.cs`)

**Immutable Fields** (Thread-Safe):
```csharp
private readonly ISourceNode _source;                              // ✅ Immutable
private readonly IStructureDefinitionSummaryProvider _provider;    // ✅ Immutable
private readonly IElementDefinitionSummary? _definition;           // ✅ Immutable
private readonly string? _parentPath;                              // ✅ Immutable
private readonly Lazy<IStructureDefinitionSummary?> _structureDefinition;  // ✅ Thread-safe
```

**Mutable Cache** (⚠️ NOT Thread-Safe):
```csharp
private Dictionary<string, IElementDefinitionSummary?>? _childDefinitionCache;  // ❌ NOT thread-safe
```

**Implications**:
- **Read operations**: Thread-safe (immutable state)
- **Cache writes** (line 193-216): **NOT thread-safe** without synchronization
- Each search parameter creates NEW child navigators via `Children()`, so cache writes happen in parallel threads

**Solution**: Replace `Dictionary` with `ConcurrentDictionary` for thread-safe caching.

### FhirEvaluationContext (`FhirEvaluationContext.cs`)

```csharp
public class FhirEvaluationContext : EvaluationContext
{
    public Func<string, ITypedElement?>? ElementResolver { get; set; }  // ⚠️ Shared
    public object? TerminologyService { get; set; }                     // ⚠️ Shared
}
```

**Current Usage** (`TypedElementSearchIndexer.cs:63-69`):
```csharp
var context = new FhirEvaluationContext();
context.ElementResolver = str => _referenceToElementResolver.Resolve(str);
context.Resource = resource;

// Shared across all search parameters ⚠️
foreach (SearchParameterInfo searchParameter in searchParameters)
{
    entries.AddRange(ProcessCompositeSearchParameter(searchParameter, resource, context));
    // context is passed to each parameter evaluation
}
```

**Problem**: Single `context` instance shared across all search parameters.

**Solutions**:
1. **Clone context per parameter** (simple, allocates more memory)
2. **Verify `context` is read-only during evaluation** (requires deep code analysis)
3. **Use `ThreadLocal<FhirEvaluationContext>`** (complex)

### FHIRPath Compiler (`FhirPathCompiler.cs`)

```csharp
public class FhirPathCompiler
{
    private readonly Tokenizer<FhirPathTokenKind> _tokenizer;  // ✅ Immutable
    private readonly bool _preserveTrivia;                     // ✅ Immutable

    public Expression Parse(string expression)  // ✅ Thread-safe (no shared mutable state)
    {
        // Parsing is stateless
    }
}
```

**Assessment**: ✅ Thread-safe (immutable state, stateless parsing)

### Converters (`ITypedElementToSearchValueConverterManager`)

**Location**: Referenced in `TypedElementSearchIndexer.cs:251`

```csharp
if (!_fhirElementTypeConverterManager.TryGetConverter(extractedValue.InstanceType,
    GetSearchValueTypeForSearchParamType(searchParameterType),
    out ITypedElementToSearchValueConverter converter))
{
    // ...
}
```

**Assumption**: Manager uses read-only dictionary lookup (thread-safe).
**Risk**: If converters have mutable state, NOT thread-safe.

## Dependency Analysis

### Parameter Independence

**Question**: Do search parameters have interdependencies?

**Evidence**:
```csharp
// Each parameter processes independently
ProcessNonCompositeSearchParameter(searchParameter, resource, context)
{
    // Evaluates FHIRPath expression on resource
    foreach (ISearchValue searchValue in ExtractSearchValues(...))
        yield return new SearchIndexEntry(searchParameterInfo, searchValue);
}
```

**Conclusion**: ✅ Search parameters are **independent** (no shared mutable state between parameters).

### Composite Search Parameters

**Special Case** (`TypedElementSearchIndexer.cs:89-155`):

```csharp
ProcessCompositeSearchParameter(SearchParameterInfo searchParameter, ITypedElement resource, EvaluationContext context)
{
    // Composite parameters reference OTHER search parameters
    for (int i = 0; i < numberOfComponents; i++)
    {
        SearchParameterInfo componentSearchParameterDefinition = searchParameter.Component[i].ResolvedSearchParameter;
        // Evaluates component expression (independent)
    }
}
```

**Assessment**: ✅ Composite parameters are self-contained (evaluate components inline, no cross-parameter dependencies).

## Performance Impact Estimation

### Theoretical Maximum Speedup

**Amdahl's Law**:
```
Speedup = 1 / (S + (P / N))

Where:
S = Sequential fraction (0.074 parsing + 0.0004 navigation = 7.4%)
P = Parallelizable fraction (0.926 indexing = 92.6%)
N = Number of cores (20 physical, 28 logical)
```

**Best Case** (20 physical cores):
```
Speedup = 1 / (0.074 + (0.926 / 20)) = 1 / 0.1203 = 8.3x
```

**Realistic Case** (accounting for overhead):
- Thread coordination overhead: ~10-15% loss
- GC pressure from parallel allocations: ~5-10% loss
- Cache contention: ~5% loss

**Estimated Speedup**: 2-3x at most (not 8x)

### Benchmarked Baseline

| Metric | Sequential | Parallel (estimated) | Improvement |
|--------|-----------|---------------------|-------------|
| **Time** | 52.7 μs | 18-26 μs | 2-3x faster |
| **Memory** | 201 KB | ~220-240 KB | 10-20% increase (overhead) |

**Benefit in Context**:
- Total request time: ~50-100ms (P50 with database)
- Database I/O: 30-70ms (70-80% of total)
- Parallel indexing saves: ~30-35 μs (0.03-0.035ms)
- **Impact on total latency**: 0.03-0.07% improvement

**Conclusion**: Negligible impact on end-to-end performance.

## Implementation Approaches

### Approach 1: Parallel.ForEach (Simple)

```csharp
public IReadOnlyCollection<SearchIndexEntry> Extract(ITypedElement resource)
{
    var entries = new ConcurrentBag<SearchIndexEntry>();
    var searchParameters = _searchParameterDefinitionManager.GetSearchParameters(resource.InstanceType);

    // Clone context per thread to avoid shared state
    var contextTemplate = new FhirEvaluationContext
    {
        ElementResolver = str => _referenceToElementResolver.Resolve(str),
        Resource = resource
    };

    Parallel.ForEach(searchParameters, searchParameter =>
    {
        // Skip resource-table parameters
        if (searchParameter.Code is "_id" or "_lastUpdated" or "_type")
            return;

        // Clone context for thread safety
        var threadContext = CloneContext(contextTemplate);

        if (searchParameter.Type == SearchParamType.Composite)
        {
            var results = ProcessCompositeSearchParameter(searchParameter, resource, threadContext);
            foreach (var result in results)
                entries.Add(result);
        }
        else
        {
            var results = ProcessNonCompositeSearchParameter(searchParameter, resource, threadContext);
            foreach (var result in results)
                entries.Add(result);
        }
    });

    return entries.ToArray();
}
```

**Pros**:
- Simple to implement
- Built-in thread pool management
- Easy to benchmark and revert

**Cons**:
- `ConcurrentBag` has overhead
- Context cloning allocates memory
- Thread pool overhead for short-lived tasks

### Approach 2: PLINQ (Functional)

```csharp
public IReadOnlyCollection<SearchIndexEntry> Extract(ITypedElement resource)
{
    var searchParameters = _searchParameterDefinitionManager.GetSearchParameters(resource.InstanceType);

    var entries = searchParameters
        .Where(sp => sp.Code is not ("_id" or "_lastUpdated" or "_type"))
        .AsParallel()
        .WithDegreeOfParallelism(Environment.ProcessorCount)
        .SelectMany(searchParameter =>
        {
            var threadContext = CreateContext(resource);

            return searchParameter.Type == SearchParamType.Composite
                ? ProcessCompositeSearchParameter(searchParameter, resource, threadContext)
                : ProcessNonCompositeSearchParameter(searchParameter, resource, threadContext);
        })
        .ToArray();

    return entries;
}
```

**Pros**:
- Declarative, functional style
- Automatic partitioning
- Less boilerplate code

**Cons**:
- Same overhead as `Parallel.ForEach`
- Harder to tune performance
- LINQ allocations

### Approach 3: Hybrid (Conditional)

```csharp
public IReadOnlyCollection<SearchIndexEntry> Extract(ITypedElement resource)
{
    var searchParameters = _searchParameterDefinitionManager.GetSearchParameters(resource.InstanceType).ToList();

    // Only parallelize if there are enough parameters to justify overhead
    const int PARALLEL_THRESHOLD = 20;

    if (searchParameters.Count < PARALLEL_THRESHOLD)
    {
        // Sequential path (existing code)
        return ExtractSequential(resource, searchParameters);
    }
    else
    {
        // Parallel path
        return ExtractParallel(resource, searchParameters);
    }
}
```

**Pros**:
- Avoids overhead for small parameter sets
- Best of both worlds
- Can A/B test performance

**Cons**:
- More code paths to maintain
- Harder to reason about

## Required Code Changes

### 1. Thread-Safe Cache in TypedElementOnSourceNode.cs

**File**: `src/Ignixa.Serialization/SourceNodes/TypedElementOnSourceNode.cs`

**Change** (line 27, 193-216):
```csharp
// OLD:
private Dictionary<string, IElementDefinitionSummary?>? _childDefinitionCache;

// NEW:
private ConcurrentDictionary<string, IElementDefinitionSummary?>? _childDefinitionCache;

// Update GetCachedChildDefinition() to use ConcurrentDictionary.GetOrAdd()
private IElementDefinitionSummary? GetCachedChildDefinition(string childName, IStructureDefinitionSummary? cachedStructureDef)
{
    if (cachedStructureDef == null)
        return null;

    _childDefinitionCache ??= new ConcurrentDictionary<string, IElementDefinitionSummary?>();

    return _childDefinitionCache.GetOrAdd(childName, key =>
    {
        var childDef = cachedStructureDef.GetElements().FirstOrDefault(e => e.ElementName == key);

        if (childDef == null)
        {
            var choiceElement = cachedStructureDef.GetElements()
                .FirstOrDefault(e => e.ElementName.EndsWith("[x]", StringComparison.Ordinal) &&
                                      key.StartsWith(e.ElementName.TrimEnd("[x]".ToCharArray()), StringComparison.Ordinal));
            if (choiceElement != null)
                childDef = choiceElement;
        }

        return childDef;
    });
}
```

### 2. Context Cloning in TypedElementSearchIndexer.cs

**File**: `src/Ignixa.Search/Indexing/TypedElementSearchIndexer.cs`

**Add Method**:
```csharp
private FhirEvaluationContext CloneContext(FhirEvaluationContext template)
{
    return new FhirEvaluationContext
    {
        ElementResolver = template.ElementResolver,
        Resource = template.Resource,
        TerminologyService = template.TerminologyService
    };
}
```

### 3. Parallel Extraction Method

**File**: `src/Ignixa.Search/Indexing/TypedElementSearchIndexer.cs`

**Add Method** (after line 87):
```csharp
/// <summary>
/// Parallel version of Extract() for high-concurrency scenarios.
/// Only beneficial when processing 20+ search parameters.
/// </summary>
private IReadOnlyCollection<SearchIndexEntry> ExtractParallel(ITypedElement resource)
{
    var entries = new ConcurrentBag<SearchIndexEntry>();
    var searchParameters = _searchParameterDefinitionManager.GetSearchParameters(resource.InstanceType);

    var contextTemplate = new FhirEvaluationContext
    {
        ElementResolver = str => _referenceToElementResolver.Resolve(str),
        Resource = resource
    };

    Parallel.ForEach(searchParameters, searchParameter =>
    {
        if (searchParameter.Code is "_id" or "_lastUpdated" or "_type")
            return;

        var threadContext = CloneContext(contextTemplate);

        IEnumerable<SearchIndexEntry> results = searchParameter.Type == SearchParamType.Composite
            ? ProcessCompositeSearchParameter(searchParameter, resource, threadContext)
            : ProcessNonCompositeSearchParameter(searchParameter, resource, threadContext);

        foreach (var result in results)
            entries.Add(result);
    });

    return entries.ToArray();
}
```

### 4. Feature Flag (Optional)

**File**: `src/Ignixa.Search/Indexing/SearchIndexerOptions.cs` (create new)

```csharp
public class SearchIndexerOptions
{
    /// <summary>
    /// Enable parallel search parameter extraction.
    /// Default: false (sequential is safer and fast enough).
    /// </summary>
    public bool EnableParallelIndexing { get; set; } = false;

    /// <summary>
    /// Minimum number of search parameters required to trigger parallel processing.
    /// Default: 20 (avoid overhead for small parameter sets).
    /// </summary>
    public int ParallelThreshold { get; set; } = 20;
}
```

## Testing Strategy

### Unit Tests

```csharp
[Fact]
public void ExtractParallel_SameResultsAsSequential()
{
    // Arrange
    var patient = LoadTestPatient();
    var indexer = CreateIndexer();

    // Act
    var sequentialResults = indexer.Extract(patient);
    var parallelResults = indexer.ExtractParallel(patient);

    // Assert
    sequentialResults.Should().BeEquivalentTo(parallelResults);
}

[Fact]
public void ExtractParallel_ThreadSafe()
{
    // Arrange
    var patient = LoadTestPatient();
    var indexer = CreateIndexer();

    // Act: Run 100 times in parallel
    var results = Enumerable.Range(0, 100)
        .AsParallel()
        .Select(_ => indexer.ExtractParallel(patient))
        .ToList();

    // Assert: All results identical
    var expected = results.First();
    results.Should().AllSatisfy(r => r.Should().BeEquivalentTo(expected));
}
```

### Performance Benchmarks

```csharp
[Benchmark(Baseline = true, Description = "Sequential (current)")]
[BenchmarkCategory("Indexing")]
public IReadOnlyCollection<SearchIndexEntry> ExtractSequential()
{
    return _searchIndexer.Extract(_patientTypedElement);
}

[Benchmark(Description = "Parallel (Parallel.ForEach)")]
[BenchmarkCategory("Indexing")]
public IReadOnlyCollection<SearchIndexEntry> ExtractParallel()
{
    return _searchIndexer.ExtractParallel(_patientTypedElement);
}

[Benchmark(Description = "Parallel (PLINQ)")]
[BenchmarkCategory("Indexing")]
public IReadOnlyCollection<SearchIndexEntry> ExtractParallelPLINQ()
{
    return _searchIndexer.ExtractParallelPLINQ(_patientTypedElement);
}
```

**Expected Results** (20-core system):
| Method | Mean | Ratio | Allocated |
|--------|------|-------|-----------|
| ExtractSequential | 52.7 μs | 1.00x | 201 KB |
| ExtractParallel | 18-26 μs | 2-3x | 220-240 KB |
| ExtractParallelPLINQ | 20-28 μs | 2-3x | 230-250 KB |

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Race Conditions** | High | Use `ConcurrentDictionary`, clone context per thread |
| **Increased Memory** | Medium | Monitor GC pressure, consider threshold-based activation |
| **Debugging Complexity** | Medium | Add extensive logging, keep sequential path as fallback |
| **Thread Pool Exhaustion** | Low | Use `MaxDegreeOfParallelism` limit |
| **GC Pressure** | Medium | Benchmark Gen2 collections, use object pooling if needed |

## Recommendations

### Priority: LOW

**Rationale**:
1. **Marginal benefit**: Saves ~30-35 μs (0.03-0.07% of total request time)
2. **Database I/O is bottleneck**: 30-70ms (70-80% of total latency)
3. **Already heavily optimized**: 44x speedup from Phases 2 & 3
4. **Complexity cost**: Thread-safety, testing, debugging overhead

### Alternative Optimizations (Higher ROI)

1. **Database Connection Pooling** (docs/investigations/sql-connection-pooling-analysis.md)
   - Impact: 30-50% reduction in cold-start latency
   - Complexity: Low (configuration change)
   - Priority: **HIGH**

2. **Selective Indexing** (Index only used parameters)
   - Impact: 50-70% reduction in indexing time (~26-37 μs savings)
   - Complexity: Medium (requires query pattern analysis)
   - Priority: **MEDIUM**

3. **Batch SQL Inserts** (SqlBulkCopy for search parameters)
   - Impact: 10-20ms savings on database writes
   - Complexity: Medium (repository refactoring)
   - Priority: **HIGH**

4. **JSON Parsing Optimization** (PipeReader, source generators)
   - Impact: 1-2 μs savings (25-50% of parsing time)
   - Complexity: Low-Medium
   - Priority: **MEDIUM**

### If Implementing Parallel Indexing

**Recommended Approach**:
1. Start with **Approach 3 (Hybrid)** - only parallelize for 20+ parameters
2. Use **Parallel.ForEach** (simpler than PLINQ)
3. Add **feature flag** for gradual rollout
4. Implement **extensive benchmarking** to validate gains

**Implementation Phases**:
1. **Phase 1**: Thread-safe cache in `TypedElementOnSourceNode` (prerequisite)
2. **Phase 2**: Add parallel extraction method with feature flag (off by default)
3. **Phase 3**: Benchmark on production workloads (A/B test)
4. **Phase 4**: Enable if > 2x speedup AND no GC regression

## Conclusion

**Summary**:
- ✅ Parallel search indexing is **technically feasible**
- ✅ Thread-safety can be achieved with `ConcurrentDictionary` and context cloning
- ⚠️ Performance gains are **limited** (2-3x at most, 0.03-0.07% of total latency)
- ❌ **Not recommended** given database I/O is the primary bottleneck

**Recommendation**:
- **Do NOT implement** parallel indexing at this time
- **Focus on**: Database connection pooling, selective indexing, SQL batch inserts
- **Revisit** parallel indexing only if:
  1. Database I/O is optimized to < 10ms (currently 30-70ms)
  2. Application layer becomes bottleneck (currently 0.057ms vs. 50-100ms total)
  3. Profiling shows indexing is > 30% of total request time (currently < 0.1%)

---

**Status**: Analysis Complete
**Decision**: Defer implementation (LOW PRIORITY)
**Next Steps**: Focus on database I/O optimization (HIGH PRIORITY)
**Revisit**: After database latency reduced to < 10ms
