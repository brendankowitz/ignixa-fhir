# Search parser benchmark comparison

**Correctness:** **Passed**

**Classification:** **Mixed**

**Blocking regression:** **No**

**Acceptance:** Accepted: correctness passed and no blocking regression was detected.

**Geometric mean time change:** -6.31%

| Case | Baseline mean (ns) | Replacement mean (ns) | Mean Δ | Baseline ops/s | Replacement ops/s | Ops/s Δ | Baseline allocated (B) | Replacement allocated (B) | Allocation Δ | Baseline Gen0 | Replacement Gen0 | Gen0 Δ |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Simple | 142.20 | 113.40 | -20.25% | 7,032,348.80 | 8,818,342.15 | +25.40% | 544.00 | 560.00 | +2.94% | 0.03 | 0.03 | +2.86% |
| Modified | 157.50 | 134.20 | -14.79% | 6,349,206.35 | 7,451,564.83 | +17.36% | 608.00 | 656.00 | +7.89% | 0.04 | 0.04 | +8.29% |
| TypedChain | 277.70 | 298.20 | +7.38% | 3,601,008.28 | 3,353,454.06 | -6.87% | 1,152.00 | 1,040.00 | -9.72% | 0.07 | 0.06 | -10.03% |
| NestedReverseChain | 560.00 | 641.40 | +14.54% | 1,785,714.29 | 1,559,089.49 | -12.69% | 2,208.00 | 2,280.00 | +3.26% | 0.13 | 0.13 | +2.97% |
| EscapedAlternative | 522.00 | 490.90 | -5.96% | 1,915,708.81 | 2,037,074.76 | +6.34% | 1,904.00 | 2,160.00 | +13.45% | 0.11 | 0.12 | +13.86% |
| Composite | 922.60 | 793.70 | -13.97% | 1,083,893.34 | 1,259,921.88 | +16.24% | 3,736.00 | 3,336.00 | -10.71% | 0.22 | 0.19 | -11.04% |

Acceptance limits: geometric-mean mean-time regression <= 10%; each individual case mean regression <= 20%; each individual case allocated-byte regression <= 25%; each individual case Gen0 regression <= 25%.
Faster remains stricter: geometric mean time <= -5%, no per-case mean > +5%, and no allocation or Gen0 increase.

Zero-denominator handling: percent change is 0% for 0->0, +∞% for 0->nonzero, and otherwise ((replacement-baseline)/baseline)*100.
