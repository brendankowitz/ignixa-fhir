---
sidebar_position: 100
title: Performance Benchmarks
---

# Performance Benchmarks

This page shows the latest performance benchmarks for Ignixa FHIR server components.

## Interactive Dashboard

View performance trends and comparisons in our interactive dashboard:

**[Open Performance Dashboard](/benchmarks-dashboard)**

The dashboard provides:
- Trend charts showing performance over time
- Side-by-side comparison of Ignixa vs Firely SDK
- Memory allocation analysis
- Filtering by benchmark category

## Latest Results

Benchmarks run automatically when a release is published and can be triggered manually.

- **Latest Run**: [View Workflow](https://github.com/brendankowitz/ignixa-fhir/actions/workflows/benchmarks.yml)
- **Interactive Dashboard**: [Performance Dashboard](/benchmarks-dashboard)
- **Raw Data**: [JSON Results](/benchmarks/latest.json)

## Running Benchmarks Manually

The benchmark workflow supports filtering to run specific benchmark categories:

1. Go to [Actions > Benchmarks](https://github.com/brendankowitz/ignixa-fhir/actions/workflows/benchmarks.yml)
2. Click "Run workflow"
3. Enter filter (e.g., `*FhirPath*` for FHIRPath benchmarks only, or leave empty for all)
4. Click "Run workflow" to start

Results are automatically committed to the repository and published to this documentation site.

## Benchmark Categories

### FhirPath Benchmarks

Measures FHIRPath expression parsing, compilation, and execution performance.

**Compilation Benchmarks:**
- Ignixa: Parse (no optimizations) - Baseline parsing performance
- Ignixa: Parse (with optimizations) - Optimized parsing with constant folding
- Firely: Compile FHIRPath expression - Firely SDK compilation time

**Evaluation Benchmarks — apples-to-apples (`Eval-*`):** *the recommended Ignixa-vs-Firely comparison.*
Both engines pre-compile the expression once and operate on an already-materialized model, so the benchmark isolates evaluation cost only:
- Simple (`Patient.name.family`)
- Complex (`Patient.name.where(use='official').given.first()`)
- Search parameter (`Observation.component.where(code.coding.code='8480-6').valueQuantity.value`)

**Execution Benchmarks (`Execution-*`) — real-world `.Select(string)` usage:**
These exercise the full call pattern (`Ignixa: IElement.Select`, `Firely: ITypedElement.Select`, and Hybrid). The Ignixa series reflects its cached `Select()`; the Hybrid series is Firely parsing + the Ignixa engine.

:::warning Firely source-backed anti-pattern
The **Firely** series in the `Execution-*` categories calls `ITypedElement.Select(string)` on a **source-backed** element (a resource parsed via `FhirJsonNode`). In Firely 6.x that extension runs `input.ToPocoNode(...)` on **every call**, re-materializing a `PocoNode` graph from the source tree each time. The result is dominated by that per-call model bridging — not by FHIRPath evaluation — and is a known performance anti-pattern.

For the fair engine-to-engine comparison, use the `Eval-*` categories (both sides pre-compiled, model materialized once). To avoid the tax in real Firely code, hold a POCO-backed element and reuse a pre-compiled `CompiledExpression`.
:::

### SqlOnFhir Benchmarks

Measures the SQL-on-FHIR ViewDefinition pipeline:
- `SqlOnFhir-Parse` - Deserialize ViewDefinition JSON to a source node (no FHIRPath compilation)
- `SqlOnFhir-Compile` - Compile a ViewDefinition to an expression tree (includes FHIRPath compilation of every column/where/forEach path). The "FHIRPath paths only" benchmark isolates the wrapper overhead, which is a small fraction of total compile cost — ViewDefinition compilation is dominated by FHIRPath compilation.
- `SqlOnFhir-Evaluate` - Evaluate ViewDefinitions against resources (flatten, forEach, WHERE, batch)
- `SqlOnFhir-Export` - Write pre-evaluated rows to CSV, NDJSON, and Parquet
- `SqlOnFhir-EndToEnd` - Full pipeline: evaluate + write per output format

### Serialization Benchmarks

Compares JSON parsing and serialization performance between Ignixa and Firely SDK:

- **Parse Small**: Minimal Patient resource (~500 bytes)
- **Parse Medium**: Observation with components (~2KB)
- **Parse Large**: Bundle with 53 entries (~100KB)
- **Serialize**: Resource serialization back to JSON

### Navigation Benchmarks

Measures resource navigation performance using different APIs:

- **JsonNode**: Direct `MutableNode["property"]` access (Ignixa)
- **ITypedElement**: `Children("property")` navigation (both Ignixa and Firely)
- **POCO**: Type-safe property access (Firely SDK)
- **Conversion**: Converting between ISourceNode and ITypedElement

### Post/Put Pipeline Benchmarks

Measures end-to-end performance of resource creation and update operations:

- Full POST pipeline (parse + validate + store + search index)
- Search index extraction
- Memory allocation patterns
- Type conversion overhead

## Understanding Results

BenchmarkDotNet provides detailed metrics for each benchmark:

- **Mean**: Average execution time per operation
- **Error**: Half of 99.9% confidence interval
- **StdDev**: Standard deviation of all measurements
- **Gen0/Gen1/Gen2**: Garbage collection counts per 1000 operations
- **Allocated**: Memory allocated per operation
- **Rank**: Relative performance ranking (1 = fastest)

### Key Metrics to Watch

1. **Mean execution time** - Lower is better
2. **Allocated memory** - Lower is better (reduces GC pressure)
3. **Gen0 collections** - Lower is better (indicates less frequent GC)
4. **Rank** - Shows relative performance within each category

## Downloading Detailed Results

Each benchmark run uploads detailed artifacts:

1. Navigate to the [workflow run](https://github.com/brendankowitz/ignixa-fhir/actions/workflows/benchmarks.yml)
2. Scroll to "Artifacts" section at the bottom
3. Download `benchmark-results-{run_number}.zip`
4. Extract to access:
   - HTML reports (interactive, visual)
   - Markdown tables (for documentation)
   - CSV data (for custom analysis)

Artifacts are retained for 90 days.

## Performance Characteristics

### Ignixa Strengths

- **Fast JSON parsing**: Uses System.Text.Json (optimized for .NET 9)
- **Low memory allocation**: JsonNode-based architecture avoids POCO overhead
- **Direct property access**: `MutableNode["property"]` is very fast
- **FHIRPath integration**: Evaluates directly over the native `IElement` model with AST + delegate compilation caching — no per-call conversion between element models. See the `Eval-*` dashboard categories for the apples-to-apples engine comparison.

### Firely SDK Strengths

- **Mature FHIRPath engine**: Hl7.FhirPath is battle-tested
- **POCO convenience**: Type-safe property access
- **Comprehensive validation**: Built-in resource validation

### Trade-offs

| Aspect | Ignixa | Firely SDK |
|--------|--------|------------|
| **JSON Parsing** | System.Text.Json (modern) | Custom parser (compatibility) |
| **Memory Model** | Mutable JsonNode | Immutable POCO |
| **Type Safety** | Runtime (JsonNode) | Compile-time (POCO) |
| **FHIRPath** | Custom implementation | Hl7.FhirPath package |
| **Validation** | Configurable tiers | Comprehensive |

## Contributing

To add new benchmarks or test data:

1. Add test data to `bench/Ignixa.Benchmarks/TestData/` (mark as EmbeddedResource)
2. Create new benchmark method with `[Benchmark]` attribute
3. Add appropriate `[BenchmarkCategory]` for filtering
4. Update `bench/Ignixa.Benchmarks/README.md` with benchmark description
5. Run locally and validate results

See [bench/Ignixa.Benchmarks/README.md](https://github.com/brendankowitz/ignixa-fhir/tree/main/bench/Ignixa.Benchmarks/README.md) for detailed guidelines.

## References

- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/)
- [Firely SDK Documentation](https://docs.fire.ly/)
- [System.Text.Json Performance](https://devblogs.microsoft.com/dotnet/try-the-new-system-text-json-api/)
- [Benchmark Source Code](https://github.com/brendankowitz/ignixa-fhir/tree/main/bench/Ignixa.Benchmarks)
