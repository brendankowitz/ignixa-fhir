# Adapter-input benchmark

## Finding

Measured at commit `86b5cce8` (clean tree). The short run observed an adapter-input win for the
measured aggregate, but not for every expression family. Against the current fhir-server indexer
path, Ignixa took **2.961 ms versus 5.102 ms** for Firely (**0.59x time**) and allocated **4.446 MB
versus 6.346 MB** (**0.70x allocation**) across the 382-expression evaluation plan. Plain paths
reversed the result: Ignixa took **0.783 ms versus 0.409 ms** (**1.92x slower**) and allocated
**1.79x** as much.

This rejects the pre-measurement expectation that the adapter would erase the aggregate win on
this workload. It does not establish a universal win: the workload mix controls the outcome, the
plain-path result is a material loss, and the short job is evidence rather than a release-grade
capacity measurement.

An earlier run of this same job at `77cb4e74` reported wall-clock times 35–45% higher in **every**
arm, including the two Firely arms the intervening commits do not touch, while reporting
byte-identical allocations for every family. The absolute times here are therefore dominated by
host variance and must not be read as a speedup delivered by those commits; the ratios and the
allocation figures are the durable content of this table.

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

Measured at commit `86b5cce8`. Times are per complete family plan. Ratios and allocation ratios use
the direct Firely indexer as baseline.

| Family | Firely indexer mean ± SD | Firely seam mean ± SD | Ignixa adapter mean ± SD | Ignixa time ratio | Ignixa allocation ratio | Reading |
|---|---:|---:|---:|---:|---:|---|
| All | 5.102 ± 0.857 ms | 4.605 ± 0.151 ms | 2.961 ± 0.270 ms | 0.59x | 0.70x | Aggregate win observed |
| Union | 4.054 ± 0.131 ms | 3.889 ± 0.015 ms | 1.702 ± 0.033 ms | 0.42x | 0.53x | Win observed; Firely arm ordering is noisy |
| `where()` | 1.314 ± 0.070 ms | 1.217 ± 0.043 ms | 0.431 ± 0.057 ms | 0.33x | 0.44x | Win observed |
| `ofType()` | 0.475 ± 0.017 ms | 0.504 ± 0.015 ms | 0.323 ± 0.020 ms | 0.68x | 0.68x | Time win; confirm with full job |
| `resolve()` | 1.637 ± 0.257 ms | 1.422 ± 0.049 ms | 0.343 ± 0.023 ms | 0.21x | 0.42x | Win observed on the resolvable Appointment fixture |
| `as()` | 0.118 ± 0.004 ms | 0.103 ± 0.001 ms | 0.096 ± 0.002 ms | 0.81x | 0.95x | Direction reversed from the `77cb4e74` run's 1.13x; 11 expressions is a thin sample |
| Plain | 0.409 ± 0.001 ms | 0.435 ± 0.005 ms | 0.783 ± 0.022 ms | 1.92x | 1.79x | Clear adapter-input loss |

Every allocation figure in this run is byte-identical to the `77cb4e74` run except Patient
lazy-tree materialization below, so the two families whose time ratio moved — `ofType()` 0.83x to
0.68x and `as()` 1.13x to 0.81x — moved without any change in allocated bytes. That is consistent
with the cheaper `element is ISystemValueElement` type test replacing a class-name string scan, but
three samples on a virtualized host cannot separate that from host variance, and the `as()` family
has 11 expressions.

The Firely seam allocated 5–10% more than the direct indexer path by family. Its time delta was not
stable in this short run: five of the seven families measured faster despite the extra wrapper, so
those Firely-versus-Firely point estimates should be treated as noise. Keeping both rows prevents the
comparison from silently choosing the Firely topology that favors a predetermined conclusion.

Compilation was measured separately and is excluded from every evaluation ratio. The short run
reported 330.4 ms / 341.4 MB for Ignixa and 378.7 ms / 951.0 MB for Firely over the distinct
five-version common corpus. Both allocation figures are unchanged from the `77cb4e74` run; both
times are materially lower, which is the same host-variance caveat as above. The indexer caches
compiled plans, so these numbers do not describe the per-write hot path.

## Adapter isolation

| Firely fixture | Root wrapper | Full lazy-tree materialization |
|---|---:|---:|
| Patient | 11.04 ns, 56 B | 14.92 us, 20,384 B |
| Observation | 11.23 ns, 56 B | 33.89 us, 55,504 B |
| Appointment | 10.18 ns, 56 B | 4.28 us, 6,744 B |

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
plan, and three populated resource fixtures, at commit `86b5cce8`, adapter-input Ignixa was about
**1.72x faster overall** than the current direct Firely indexer and allocated about **30% less**,
while plain paths were about **1.92x slower** and allocated about **79% more**. It does not license
a 3,220x Phase 3 claim, a portable production throughput claim, or enablement without the parity
gate and a full benchmark run on representative deployment hardware.

Benchmarks are not unit tests, so there is no regression/guard table. Verification is the Release
build, the executed BenchmarkDotNet job above, and the `Ignixa.FhirPath.Tests` result at the same
commit.
