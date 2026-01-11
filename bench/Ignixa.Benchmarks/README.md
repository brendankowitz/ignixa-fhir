# Ignixa FHIR Benchmarks

Performance benchmarks comparing Ignixa's FHIR implementation against the Firely SDK (Hl7.Fhir.R4 6.0.0).

## Overview

This benchmark project measures performance across three key areas:

1. **Serialization** - JSON parsing and serialization performance
2. **Navigation** - Resource navigation using different APIs (JsonNode, ITypedElement, POCO)
3. **FHIRPath** - FHIRPath expression evaluation and compilation

## Running the Benchmarks

### Run All Benchmarks

```bash
cd bench/Ignixa.Benchmarks
dotnet run -c Release
```

### Run Specific Benchmark Class

```bash
# Serialization only
dotnet run -c Release --filter *SerializationBenchmarks*

# Navigation only
dotnet run -c Release --filter *NavigationBenchmarks*

# FHIRPath only
dotnet run -c Release --filter *FhirPathBenchmarks*
```

### Run by Category

```bash
# Only "Small" resource benchmarks
dotnet run -c Release --filter *Small*

# Only "Parse" benchmarks
dotnet run -c Release --filter *Parse*

# Only "Simple" FHIRPath benchmarks
dotnet run -c Release --filter *Simple*
```

## Benchmark Categories

### SerializationBenchmarks.cs

Compares JSON parsing and serialization performance:

| Category | Description | Test Data |
|----------|-------------|-----------|
| **Parse Small** | Parse minimal Patient (~500 bytes) | patient-small.json |
| **Parse Medium** | Parse Observation with components (~2KB) | observation-medium.json |
| **Parse Large** | Parse Bundle with 53 entries (~100KB) | bundle-large.json |
| **Serialize** | Serialize resources back to JSON | Same as above |

**Comparison Points**:
- **Ignixa**: `JsonSerializer.Deserialize<ResourceJsonNode>()` (System.Text.Json)
- **Firely SDK**: `FhirJsonNode.Parse()` (ISourceNode) + POCO parsing

### NavigationBenchmarks.cs

Compares resource navigation performance:

| Category | Description |
|----------|-------------|
| **Simple** | Access simple property (`status`) |
| **Nested** | Access nested object (`code.coding[0].code`) |
| **Array** | Access array element (`component[0].valueQuantity.value`) |
| **Conversion** | Convert to ISourceNode / ITypedElement |

**Comparison Points**:
- **Ignixa JsonNode**: Direct `MutableNode["property"]` access
- **Ignixa ITypedElement**: `Children("property").FirstOrDefault()`
- **Firely POCO**: `resource.Property` access
- **Firely ITypedElement**: `Children("property").FirstOrDefault()`

### FhirPathBenchmarks.cs

Compares FHIRPath parsing, compilation, and execution performance.

**IMPORTANT**: These benchmarks separate compilation from execution to ensure fair comparison:
- **Compilation benchmarks**: Measure time to parse and compile an expression (no caching)
- **Execution benchmarks**: Use pre-compiled/cached expressions (caches warmed up in GlobalSetup)

| Category | Expression | Description |
|----------|------------|-------------|
| **Compilation** | `Patient.name.where(use='official').given.first()` | Parsing + compilation time (no caching) |
| **Execution-Simple** | `Patient.name.family` | Simple property access (with compiled expression) |
| **Execution-Array** | `Patient.name[0].given` | Array indexing (with compiled expression) |
| **Execution-Complex** | `Patient.name.where(use='official').given.first()` | Filtering + navigation (with compiled expression) |
| **Execution-SearchParam** | `Observation.component.where(code.coding.code='8480-6').valueQuantity.value` | Realistic search parameter extraction (with compiled expression) |
| **Execution-Scalar** | `Patient.birthDate` | Scalar value extraction (with compiled expression) |

**Comparison Points**:
- **Ignixa Compilation**: `FhirPathParser.Parse()` (AST + optional delegate compilation)
- **Firely Compilation**: `FhirPathCompiler.Compile()` (compiled expression)
- **Ignixa Execution**: `TypedElementExtensions.Select()` (uses cached AST + compiled delegate)
- **Firely Execution**: `ITypedElement.Select()` extension from Hl7.FhirPath (uses cached compiled expression)

## Test Data

All test resources are embedded in the assembly from `TestData/`:

| File | Size | Description |
|------|------|-------------|
| `patient-small.json` | ~500 bytes | Minimal Patient with id, name, gender, birthDate |
| `observation-medium.json` | ~2KB | Blood pressure Observation with components, references |
| `bundle-large.json` | ~100KB | Transaction Bundle with 53 entries (10 patients, 30 observations, 4 conditions, 4 medications, etc.) |

## Understanding the Results

BenchmarkDotNet outputs include:

- **Mean** - Average execution time per operation
- **Error** - Half of 99.9% confidence interval
- **StdDev** - Standard deviation of all measurements
- **Gen0/Gen1/Gen2** - Garbage collection counts per 1000 operations
- **Allocated** - Allocated memory per operation
- **Rank** - Relative ranking (1 = fastest)

### Key Metrics to Watch

1. **Mean execution time** - Lower is better
2. **Allocated memory** - Lower is better (reduces GC pressure)
3. **Gen0 collections** - Lower is better (indicates less frequent GC)
4. **Rank** - Shows relative performance within each category

## Expected Performance Characteristics

### Ignixa Strengths

- **Fast JSON parsing** - Uses System.Text.Json (optimized for .NET 9)
- **Low memory allocation** - JsonNode-based architecture avoids POCO overhead
- **Direct property access** - MutableNode["property"] is very fast
- **FHIRPath integration** - Tight integration with IAnnotated pattern

### Firely SDK Strengths

- **Mature FHIRPath engine** - Hl7.FhirPath is battle-tested
- **POCO convenience** - Type-safe property access
- **Comprehensive validation** - Built-in resource validation

### Trade-offs

| Aspect | Ignixa | Firely SDK |
|--------|--------|------------|
| **JSON Parsing** | System.Text.Json (modern) | Custom parser (compatibility) |
| **Memory Model** | Mutable JsonNode | Immutable POCO |
| **Type Safety** | Runtime (JsonNode) | Compile-time (POCO) |
| **FHIRPath** | Custom implementation | Hl7.FhirPath package |
| **Validation** | Minimal (prototype) | Comprehensive |

## Baseline Expectations

Initial estimates (to be validated):

- **Parsing**: Ignixa likely 10-30% faster (System.Text.Json advantage)
- **Navigation (JsonNode)**: Ignixa likely 2-5x faster than POCO conversion
- **Navigation (ITypedElement)**: Similar performance (same interface)
- **FHIRPath**: Firely likely faster initially (mature implementation)
- **Memory**: Ignixa likely 30-50% lower allocations (no POCO overhead)

## Output Files

Benchmark results are saved to:

```
bench/Ignixa.Benchmarks/BenchmarkDotNet.Artifacts/results/
├── *.html              # HTML report
├── *.md                # Markdown report (for docs)
├── *.csv               # Raw data
└── *-report-github.md  # GitHub-formatted report
```

## Contributing Benchmarks

To add new benchmarks:

1. Add new test data to `TestData/` (mark as EmbeddedResource)
2. Create new benchmark method with `[Benchmark]` attribute
3. Add appropriate `[BenchmarkCategory]` for filtering
4. Run and validate results
5. Update this README with new benchmark description

## Notes

- **Release mode only** - Always use `-c Release` for accurate measurements
- **Multiple iterations** - BenchmarkDotNet runs multiple warmup + measurement iterations
- **Process isolation** - Each benchmark class runs in a separate process
- **Statistical analysis** - Results include confidence intervals and outlier detection

## References

- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/)
- [Firely SDK Documentation](https://docs.fire.ly/)
- [System.Text.Json Performance](https://devblogs.microsoft.com/dotnet/try-the-new-system-text-json-api/)
