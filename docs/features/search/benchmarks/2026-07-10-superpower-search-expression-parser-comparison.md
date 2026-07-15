# Search parser benchmark comparison

**Correctness:** **Passed**

**Classification:** **Slower**

**Blocking regression:** **Yes**

**Acceptance:** Blocked: regression exceeds the 10% blocking threshold. Investigate and obtain explicit user acceptance.

**Geometric mean time change:** +856.07%

| Case | Baseline mean (ns) | Replacement mean (ns) | Mean Δ | Baseline ops/s | Replacement ops/s | Ops/s Δ | Baseline allocated (B) | Replacement allocated (B) | Allocation Δ | Baseline Gen0 | Replacement Gen0 | Gen0 Δ |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Simple | 142.20 | 1,774.00 | +1147.54% | 7,032,348.80 | 563,697.86 | -91.98% | 544.00 | 4,597.76 | +745.18% | 0.03 | 0.26 | +723.49% |
| Modified | 157.50 | 2,100.00 | +1233.33% | 6,349,206.35 | 476,190.48 | -92.50% | 608.00 | 5,376.00 | +784.21% | 0.04 | 0.31 | +772.00% |
| TypedChain | 277.70 | 2,949.00 | +961.94% | 3,601,008.28 | 339,098.00 | -90.58% | 1,152.00 | 7,434.24 | +545.33% | 0.07 | 0.43 | +539.52% |
| NestedReverseChain | 560.00 | 6,886.00 | +1129.64% | 1,785,714.29 | 145,222.19 | -91.87% | 2,208.00 | 13,506.56 | +511.71% | 0.13 | 0.76 | +496.95% |
| EscapedAlternative | 522.00 | 3,485.00 | +567.62% | 1,915,708.81 | 286,944.05 | -85.02% | 1,904.00 | 8,263.68 | +334.02% | 0.11 | 0.47 | +331.18% |
| Composite | 922.60 | 4,859.00 | +426.66% | 1,083,893.34 | 205,803.66 | -81.01% | 3,736.00 | 12,042.24 | +222.33% | 0.22 | 0.67 | +210.12% |

Thresholds: Faster requires geometric mean time <= -5%, no per-case mean > +5%, and no allocation or Gen0 increase. Slower is geometric mean >= +5%. Equivalent within 5% requires |geometric mean| < 5% and no blocking regression. Any per-case mean/allocation/Gen0 increase > +10% is a blocking regression.

Zero-denominator handling: percent change is 0% for 0->0, +∞% for 0->nonzero, and otherwise ((replacement-baseline)/baseline)*100.
