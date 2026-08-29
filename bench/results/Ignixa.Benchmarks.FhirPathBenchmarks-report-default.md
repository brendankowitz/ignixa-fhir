
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9106/25H2/2025Update/HudsonValley2) (Hyper-V)
AMD EPYC 7763 2.44GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.303
  [Host]    : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
  .NET 10.0 : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3

Job=.NET 10.0  Runtime=.NET 10.0  

 Method                                                             | Mean         | Error        | StdDev       | Median       | Rank | Gen0    | Gen1   | Allocated |
------------------------------------------------------------------- |-------------:|-------------:|-------------:|-------------:|-----:|--------:|-------:|----------:|
 'Ignixa: Parse (no optimizations)'                                 | 174,882.4 ns |  7,192.44 ns | 21,207.07 ns | 172,354.2 ns |   13 |  2.9297 |      - |   55249 B |
 'Ignixa: Parse (with optimizations)'                               | 163,752.5 ns |  6,576.50 ns | 19,287.74 ns | 158,366.1 ns |   13 |  2.9297 |      - |   55313 B |
 'Firely: Compile FHIRPath expression'                              | 332,638.9 ns |  7,623.02 ns | 21,748.91 ns | 329,930.1 ns |   14 | 38.5742 | 9.7656 |  648776 B |
 'Ignixa: Eval complex (pre-compiled, eval only)'                   |     535.4 ns |     10.72 ns |     14.31 ns |     532.8 ns |    4 |  0.0381 |      - |     648 B |
 'Firely: Eval complex (pre-compiled, eval only)'                   |   6,461.7 ns |    130.49 ns |    384.76 ns |   6,399.1 ns |   11 |  0.5646 |      - |    9496 B |
 'Ignixa: Eval searchparam (pre-compiled, eval only)'               |   1,547.0 ns |     30.65 ns |     65.32 ns |   1,536.8 ns |    9 |  0.0744 |      - |    1248 B |
 'Firely: Eval searchparam (pre-compiled, eval only)'               |  11,117.0 ns |    221.79 ns |    607.15 ns |  10,870.6 ns |   12 |  1.0529 |      - |   17696 B |
 'Ignixa: Eval simple (pre-compiled, eval only)'                    |     242.8 ns |      4.86 ns |     11.93 ns |     242.2 ns |    2 |  0.0148 |      - |     248 B |
 'Firely: Eval simple (pre-compiled, eval only)'                    |   2,082.5 ns |     44.79 ns |    126.33 ns |   2,066.3 ns |   10 |  0.2670 |      - |    4520 B |
 'Ignixa: Array indexing (Patient.name[0].given)'                   |     747.1 ns |     16.78 ns |     48.68 ns |     736.0 ns |    6 |  0.0820 |      - |    1376 B |
 'Firely: Array indexing (Patient.name[0].given)'                   | 852,778.4 ns | 22,507.08 ns | 64,938.07 ns | 834,537.6 ns |   15 |  3.9063 | 1.9531 |   84854 B |
 'Hybrid: Array indexing (Firely parse + Ignixa eval)'              |     690.4 ns |     12.57 ns |     31.77 ns |     687.0 ns |    5 |  0.0849 |      - |    1424 B |
 'Ignixa: Complex navigation (where + first)'                       |     657.5 ns |     16.19 ns |     47.49 ns |     641.9 ns |    5 |  0.0525 |      - |     880 B |
 'Firely: Complex navigation (where + first)'                       | 886,910.7 ns | 25,981.95 ns | 75,790.57 ns | 875,793.4 ns |   15 |  3.9063 | 1.9531 |   85750 B |
 'Hybrid: Complex navigation (Firely parse + Ignixa eval)'          |     536.7 ns |     10.82 ns |     27.14 ns |     532.7 ns |    4 |  0.0563 |      - |     952 B |
 'Ignixa: Scalar extraction (Patient.birthDate)'                    |     220.9 ns |      4.23 ns |      9.38 ns |     218.6 ns |    2 |  0.0248 |      - |     416 B |
 'Firely: Scalar extraction (Patient.birthDate)'                    | 791,821.9 ns | 14,992.66 ns | 16,664.30 ns | 787,323.7 ns |   15 |  3.9063 | 1.9531 |   80688 B |
 'Hybrid: Scalar extraction (Firely parse + Ignixa eval)'           |     152.1 ns |      3.10 ns |      8.00 ns |     150.8 ns |    1 |  0.0262 |      - |     440 B |
 'Ignixa: Search parameter extraction (component value)'            |   1,455.5 ns |     32.28 ns |     93.65 ns |   1,427.6 ns |    8 |  0.0877 |      - |    1480 B |
 'Firely: Search parameter extraction (component value)'            | 155,289.7 ns |  4,373.28 ns | 12,547.77 ns | 150,923.3 ns |   13 | 12.6953 | 2.9297 |  222307 B |
 'Hybrid: Search parameter extraction (Firely parse + Ignixa eval)' |   1,007.5 ns |     19.78 ns |     17.54 ns |   1,007.7 ns |    7 |  0.0954 |      - |    1608 B |
 'Ignixa: Simple FHIRPath (Patient.name.family)'                    |     286.2 ns |      5.75 ns |     11.88 ns |     285.3 ns |    3 |  0.0286 |      - |     480 B |
 'Firely: Simple FHIRPath (Patient.name.family)'                    | 799,983.0 ns | 15,265.30 ns | 14,279.17 ns | 798,313.5 ns |   15 |  3.9063 | 1.9531 |   80824 B |
 'Hybrid: Simple FHIRPath (Firely parse + Ignixa eval)'             |     235.9 ns |      6.39 ns |     18.02 ns |     233.4 ns |    2 |  0.0315 |      - |     528 B |
