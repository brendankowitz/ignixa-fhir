export interface BenchmarkStatistics {
  Mean: number;
  StdErr: number;
  StdDev: number;
  Min: number;
  Q1: number;
  Median: number;
  Q3: number;
  Max: number;
  InterquartileRange: number;
  Outliers: number[];
  LowerFence: number;
  UpperFence: number;
  ConfidenceInterval: {
    N: number;
    Mean: number;
    StandardError: number;
    Level: number;
    Margin: number;
    Lower: number;
    Upper: number;
  };
  Percentiles: {
    P0: number;
    P25: number;
    P50: number;
    P67: number;
    P80: number;
    P85: number;
    P90: number;
    P95: number;
    P100: number;
  };
}

export interface BenchmarkMemory {
  Gen0Collections: number;
  Gen1Collections: number;
  Gen2Collections: number;
  TotalOperations: number;
  BytesAllocatedPerOperation: number;
}

export interface BenchmarkResult {
  DisplayInfo: string;
  Namespace: string;
  Type: string;
  Method: string;
  MethodTitle: string;
  Parameters: string;
  FullName: string;
  HardwareIntrinsics: string;
  Statistics: BenchmarkStatistics;
  Memory?: BenchmarkMemory;
  Measurements?: Array<{
    IterationMode: string;
    IterationStage: string;
    LaunchIndex: number;
    IterationIndex: number;
    Operations: number;
    Nanoseconds: number;
  }>;
  Rank?: number;
}

export interface BenchmarkRun {
  Title: string;
  HostEnvironmentInfo: {
    BenchmarkDotNetCaption: string;
    BenchmarkDotNetVersion: string;
    OsVersion: string;
    ProcessorName: string;
    PhysicalProcessorCount: number;
    PhysicalCoreCount: number;
    LogicalCoreCount: number;
    RuntimeVersion: string;
    Architecture: string;
    HasAttachedDebugger: boolean;
    HasRyuJit: boolean;
    Configuration: string;
    DotNetCliVersion: string;
    ChronometerFrequency: {
      Hertz: number;
    };
    HardwareTimerKind: string;
  };
  Benchmarks: BenchmarkResult[];
}

export interface BenchmarkFile {
  filename: string;
  timestamp: Date;
  data: BenchmarkRun;
}

export interface ProcessedBenchmark {
  name: string;
  method: string;
  category: string;
  implementation: 'Ignixa' | 'Firely';
  meanNs: number;
  meanUs: number;
  meanMs: number;
  stdDev: number;
  allocatedBytes: number;
  allocatedKb: number;
  gen0: number;
  gen1: number;
  gen2: number;
  rank?: number;
  timestamp: Date;
  runId: string;
}

export interface ChartDataPoint {
  date: string;
  timestamp: number;
  runId: string;
  ignixa?: number;
  firely?: number;
  hybrid?: number;
  ignixaStdDev?: number;
  firelyStdDev?: number;
  hybridStdDev?: number;
  ignixaAlloc?: number;
  firelyAlloc?: number;
  hybridAlloc?: number;
}

export interface CategoryData {
  name: string;
  benchmarks: ProcessedBenchmark[];
  chartData: ChartDataPoint[];
}

export type TimeUnit = 'ns' | 'us' | 'ms';
export type MemoryUnit = 'B' | 'KB' | 'MB';

export interface DashboardFilters {
  category: string | null;
  dateRange: [Date | null, Date | null];
  showMemory: boolean;
}
