# Adapter-input benchmark

## Finding

The short run observed an adapter-input win for the measured aggregate, but not for every
expression family. Against the current fhir-server indexer path, Ignixa took **5.449 ms versus
7.954 ms** for Firely (**0.69x time**) and allocated **4.446 MB versus 6.346 MB** (**0.70x
allocation**) across the 382-expression evaluation plan. Plain paths reversed the result:
Ignixa took **1.366 ms versus 0.633 ms** (**2.16x slower**) and allocated **1.79x** as much.

This rejects the pre-measurement expectation that the adapter would erase the aggregate win on
this workload. It does not establish a universal win: the workload mix controls the outcome, the
plain-path result is a material loss, and the short job is evidence rather than a release-grade
capacity measurement.

## What was measured

Setup parses the Patient, Observation, and Appointment JSON fixtures once with Firely 5.11.4 into
Firely POCO-backed `ITypedElement` instances. Every arm receives those same instances. Corpus
loading, JSON parsing, FHIRPath parsing/compilation, plan construction, and explicit JIT warmup are
outside the evaluation measurements.

- **Firely indexer:** one `FhirEvaluationContext` per resource extraction, cached compiled delegate
  invoked directly for each expression. This is the current `TypedElementSearchIndexer` path.
- **Firely seam:** the same context lifetime and compiled delegates, plus `ToScopedNode()` per
  expression. This is the behavior of Firely 5.11.4's extension-method path and the proposed seam.
- **Ignixa seam:** one Firely context per resource extraction; then, per expression,
  `ToIgnixaElement()`, the immutable Ignixa context/resolver bridge, precompiled evaluation,
  `TypedElementAdapter` construction for every result, and result enumeration/value access.

The benchmark registers Firely's FHIR symbol extensions before loading the corpus. Without that
production registration, `resolve()` expressions are compile failures and silently disappear from
the comparison.

The corpus contains 6,616 shipped search parameters across STU3, R4, R4B, R5, and R6: 2,396
distinct expressions, of which both engines compile 2,395. The evaluation plan includes expressions
applicable to the three populated fixtures: All 382, union 144, `where()` 29, `ofType()` 30,
`resolve()` 19, `as()` 11, and plain paths 207. Feature families overlap; `All` contains each plan
entry once, while `Plain` means none of the five named constructs occurs. This is not a resource
fixture for every FHIR type. The project pins the R4 POCO package, so expressions contributed by
the other four definition sets run against the R4 fixture of the same resource type. The result
must not be generalized to another FHIR release or production resource mix without rerunning
against that mix.

## Environment and configuration

- BenchmarkDotNet 0.15.8
- Windows 11 10.0.26200.9106, Hyper-V virtual machine
- AMD EPYC 7763 2.44 GHz, 1 CPU, 8 physical / 16 logical cores
- .NET SDK 10.0.303
- .NET runtime 10.0.11, x64 RyuJIT x86-64-v3
- Concurrent workstation GC
- `ShortRun`: 1 launch, 3 warmup iterations, 3 measurement iterations
- Release build

Command:

```powershell
dotnet run -c Release --no-build `
  --project bench/Ignixa.Benchmarks.Firely5/Ignixa.Benchmarks.Firely5.csproj `
  -- quick --filter "*IndexingHeadToHeadBenchmarks*"
```

Omit `quick` for the harness's full BenchmarkDotNet default job.

The host is virtualized and the short job has only three samples. BenchmarkDotNet's 99.9%
confidence intervals are consequently wider than the means for several cases. The table reports
mean and standard deviation, not false precision; family results with overlapping distributions
are marked as within short-run noise.

## Evaluation results

Times are per complete family plan. Ratios and allocation ratios use the direct Firely indexer as
baseline.

| Family | Firely indexer mean ± SD | Firely seam mean ± SD | Ignixa adapter mean ± SD | Ignixa time ratio | Ignixa allocation ratio | Reading |
|---|---:|---:|---:|---:|---:|---|
| All | 7.954 ± 0.198 ms | 8.493 ± 0.194 ms | 5.449 ± 0.224 ms | 0.69x | 0.70x | Aggregate win observed |
| Union | 7.695 ± 0.644 ms | 6.852 ± 0.126 ms | 3.224 ± 0.229 ms | 0.42x | 0.53x | Win observed; Firely arm ordering is noisy |
| `where()` | 1.982 ± 0.074 ms | 2.125 ± 0.026 ms | 0.683 ± 0.021 ms | 0.34x | 0.44x | Win observed |
| `ofType()` | 0.605 ± 0.034 ms | 0.815 ± 0.086 ms | 0.504 ± 0.017 ms | 0.83x | 0.68x | Small time win; confirm with full job |
| `resolve()` | 1.710 ± 0.056 ms | 1.886 ± 0.054 ms | 0.582 ± 0.034 ms | 0.34x | 0.42x | Win observed on the resolvable Appointment fixture |
| `as()` | 0.138 ± 0.020 ms | 0.169 ± 0.008 ms | 0.153 ± 0.011 ms | 1.13x | 0.95x | Time difference is within short-run noise |
| Plain | 0.633 ± 0.016 ms | 0.605 ± 0.077 ms | 1.366 ± 0.109 ms | 2.16x | 1.79x | Clear adapter-input loss |

The Firely seam allocated 5–10% more than the direct indexer path by family. Its time delta was not
stable in this short run: union and plain paths measured faster despite the extra wrapper, so those
Firely-versus-Firely point estimates should be treated as noise. Keeping both rows prevents the
comparison from silently choosing the Firely topology that favors a predetermined conclusion.

Compilation was measured separately and is excluded from every evaluation ratio. The short run
reported 586.6 ms / 341.4 MB for Ignixa and 648.3 ms / 951.0 MB for Firely over the distinct
five-version common corpus. The indexer caches compiled plans, so these numbers do not describe the
per-write hot path.

## Adapter isolation

| Firely fixture | Root wrapper | Full lazy-tree materialization |
|---|---:|---:|
| Patient | 15.38 ns, 56 B | 22.03 us, 20,256 B |
| Observation | 15.92 ns, 56 B | 59.31 us, 55,504 B |
| Appointment | 17.22 ns, 56 B | 6.63 us, 6,744 B |

`ToIgnixaElement()` itself is a small root allocation. The materialized child adapters are the
meaningful cost, and they scale with the portion of the Firely tree traversed. Moving ingress to
native Ignixa elements in Phase 4 would remove that traversal/allocation tax; these isolation
numbers do not predict the total Phase 4 speedup because evaluation still dominates some families.

## Claim boundary

`bench/Ignixa.Benchmarks/FhirPathBenchmarks.cs` builds its headline Ignixa inputs natively from
Ignixa `ResourceJsonNode` instances. Its hybrid inputs are wrapped once during setup. The previous
`IndexingHeadToHeadBenchmarks` also parsed a separate native Ignixa tree. Those benchmarks can
support native-element claims such as the published **3,220x** figure; they do not describe this
per-call adapter-input topology.

This run licenses only this statement: on this machine, runtime, short-job configuration, corpus
plan, and three populated resource fixtures, adapter-input Ignixa was about **1.46x faster overall**
than the current direct Firely indexer and allocated about **30% less**, while plain paths were
about **2.16x slower** and allocated about **79% more**. It does not license a 3,220x Phase 3 claim,
a portable production throughput claim, or enablement without the parity gate and a full benchmark
run on representative deployment hardware.

Benchmarks are not unit tests, so there is no regression/guard table. Verification is the Release
build, the executed BenchmarkDotNet job above, and the unchanged `Ignixa.FhirPath.Tests` result.
