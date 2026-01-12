import React, { useState, useEffect, useMemo } from 'react';
import useBaseUrl from '@docusaurus/useBaseUrl';
import {
  LineChart,
  Line,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
  TooltipProps,
} from 'recharts';
import type {
  BenchmarkRun,
  BenchmarkFile,
  ProcessedBenchmark,
  ChartDataPoint,
  CategoryData,
} from './types';
import styles from './styles.module.css';

interface LatestMetadata {
  latestRun: string;
  runs: Array<{
    directory: string;
    branch: string;
    commit: string;
    timestamp: string;
    run_number?: string;
    tag?: string;
    description?: string;
  }>;
}

interface RunMetadata {
  timestamp: string;
  commit: string;
  branch: string;
  run_number?: string;
  tag?: string;
  description?: string;
}

const CATEGORY_MAP: Record<string, string> = {
  IgnixaParseBaseline: 'Compilation',
  IgnixaParseOptimized: 'Compilation',
  FirelyCompile: 'Compilation',
  IgnixaSimple: 'Execution-Simple',
  FirelySimple: 'Execution-Simple',
  HybridSimple: 'Execution-Simple',
  IgnixaArray: 'Execution-Array',
  FirelyArray: 'Execution-Array',
  HybridArray: 'Execution-Array',
  IgnixaComplex: 'Execution-Complex',
  FirelyComplex: 'Execution-Complex',
  HybridComplex: 'Execution-Complex',
  IgnixaSearchParam: 'Execution-SearchParam',
  FirelySearchParam: 'Execution-SearchParam',
  HybridSearchParam: 'Execution-SearchParam',
  IgnixaScalar: 'Execution-Scalar',
  FirelyScalar: 'Execution-Scalar',
  HybridScalar: 'Execution-Scalar',
};

const CATEGORY_DESCRIPTIONS: Record<string, string> = {
  'Compilation': 'Parsing FHIRPath expressions and compiling them into executable form. Lower is better.',
  'Execution-Simple': 'Basic property access like Patient.name.family. Tests simple navigation performance.',
  'Execution-Array': 'Array indexing operations like Patient.name[0].given. Tests collection access patterns.',
  'Execution-Complex': 'Complex navigation with where() and first() functions. Tests filtering and advanced queries.',
  'Execution-SearchParam': 'Search parameter extraction from resources. Tests real-world FHIR search scenarios.',
  'Execution-Scalar': 'Scalar value extraction like Patient.birthDate. Tests primitive value access.',
  'Hybrid': 'Firely SDK parsing + Ignixa FHIRPath engine. Demonstrates drop-in FHIRPath performance improvement.',
};

function detectImplementation(method: string, displayInfo: string): 'Ignixa' | 'Firely' | 'Hybrid' {
  if (method.startsWith('Hybrid') || displayInfo.toLowerCase().includes('hybrid')) {
    return 'Hybrid';
  }
  if (method.startsWith('Ignixa') || displayInfo.toLowerCase().includes('ignixa')) {
    return 'Ignixa';
  }
  return 'Firely';
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(2)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

function formatNanoseconds(ns: number): string {
  if (ns < 1000) return `${ns.toFixed(2)} ns`;
  if (ns < 1000000) return `${(ns / 1000).toFixed(2)} us`;
  return `${(ns / 1000000).toFixed(2)} ms`;
}

function formatDate(date: Date): string {
  return date.toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
}

function formatDateWithTime(date: Date): string {
  return date.toLocaleString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    hour12: true,
  });
}

function formatMultiplier(value: number): string {
  // For large multipliers (>=10x), round to whole number with thousand separators
  // For smaller multipliers (<10x), keep 1 decimal place
  if (value >= 10) {
    return `${Math.round(value).toLocaleString('en-US')}x`;
  }
  return `${value.toFixed(1)}x`;
}

function parseTimestampFromTitle(title: string): Date {
  // Match YYYYMMDD-HHMMSS format in title
  const match = title.match(/(\d{8})-(\d{6})/);
  if (match) {
    const dateStr = match[1];
    const timeStr = match[2];
    const year = parseInt(dateStr.slice(0, 4), 10);
    const month = parseInt(dateStr.slice(4, 6), 10) - 1;
    const day = parseInt(dateStr.slice(6, 8), 10);
    const hour = parseInt(timeStr.slice(0, 2), 10);
    const minute = parseInt(timeStr.slice(2, 4), 10);
    const second = parseInt(timeStr.slice(4, 6), 10);
    return new Date(year, month, day, hour, minute, second);
  }
  return new Date();
}

function extractCleanName(displayInfo: string): string {
  // Extract from BenchmarkDotNet format: "ClassName.'Display Name': .NET 9.0(...)"
  const match = displayInfo.match(/'([^']+)'/);
  if (match) {
    return match[1];
  }
  return displayInfo;
}

interface CustomTooltipProps extends TooltipProps<number, string> {
  showMemory: boolean;
}

function CustomTooltip({ active, payload, label, showMemory }: CustomTooltipProps) {
  if (!active || !payload || payload.length === 0) {
    return null;
  }

  return (
    <div className={styles.tooltip}>
      <p className={styles.tooltipLabel}>{label}</p>
      {payload.map((entry, index) => {
        const value = entry.value as number;
        const dataPoint = entry.payload as any;
        const dataKey = entry.dataKey as string;

        // Get standard deviation - check for specific keys first, then generic pattern
        let stdDev: number | undefined;
        if (dataKey === 'ignixa') {
          stdDev = dataPoint.ignixaStdDev;
        } else if (dataKey === 'firely') {
          stdDev = dataPoint.firelyStdDev;
        } else if (dataKey === 'hybrid') {
          stdDev = dataPoint.hybridStdDev;
        } else if (dataKey.endsWith('Alloc')) {
          // For allocation metrics, we don't have stdDev
          stdDev = undefined;
        } else {
          // For dynamic variant labels (All Benchmarks tab), check for ${variantLabel}_stdDev
          stdDev = dataPoint[`${dataKey}_stdDev`];
        }

        const formattedValue = showMemory ? formatBytes(value) : formatNanoseconds(value);
        const formattedStdDev = stdDev !== undefined && !showMemory
          ? ` ± ${formatNanoseconds(stdDev)}`
          : '';

        return (
          <p key={index} style={{ color: entry.color }} className={styles.tooltipEntry}>
            {entry.name}: {formattedValue}{formattedStdDev}
          </p>
        );
      })}
    </div>
  );
}

export default function BenchmarkDashboard(): JSX.Element {
  const [benchmarkFiles, setBenchmarkFiles] = useState<BenchmarkFile[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedCategory, setSelectedCategory] = useState<string>('All');
  const [activeTab, setActiveTab] = useState<'fhirpath' | 'all'>('fhirpath');
  const [selectedSuite, setSelectedSuite] = useState<string>('All');
  const baseUrl = useBaseUrl('/');

  useEffect(() => {
    async function loadBenchmarks() {
      try {
        // Load latest.json to get all benchmark runs
        const metadataResponse = await fetch(`${baseUrl}benchmarks/latest.json`);
        if (!metadataResponse.ok) {
          throw new Error('Failed to load benchmark metadata');
        }
        const metadata: LatestMetadata = await metadataResponse.json();

        // Load benchmark files from each run directory
        const allFiles: BenchmarkFile[] = [];

        for (const run of metadata.runs) {
          const runDir = run.directory;

          // List of expected benchmark files
          const benchmarkTypes = [
            'FhirPathBenchmarks',
            'NavigationBenchmarks',
            'SerializationBenchmarks'
          ];

          for (const benchType of benchmarkTypes) {
            const filename = `Ignixa.Benchmarks.${benchType}-report-full-compressed.json`;
            const filePath = `${baseUrl}benchmarks/${runDir}/${filename}`;

            try {
              const response = await fetch(filePath);
              if (!response.ok) {
                // File might not exist for this run (e.g., ValidationBenchmarks failed in baseline)
                console.log(`Skipping ${filePath}: ${response.status}`);
                continue;
              }

              const data: BenchmarkRun = await response.json();
              allFiles.push({
                filename: `${runDir}/${filename}`,
                timestamp: new Date(run.timestamp),
                data,
              });
            } catch (err) {
              console.log(`Failed to load ${filePath}:`, err);
              // Continue with other files
            }
          }
        }

        allFiles.sort((a, b) => a.timestamp.getTime() - b.timestamp.getTime());
        console.log('Loaded benchmark files:', allFiles.length);
        console.log('Sample benchmark:', allFiles[0]?.data.Benchmarks[0]);
        setBenchmarkFiles(allFiles);
      } catch (err) {
        console.error('Error loading benchmarks:', err);
        setError(err instanceof Error ? err.message : 'Unknown error');
      } finally {
        setLoading(false);
      }
    }

    loadBenchmarks();
  }, [baseUrl]);

  const processedData = useMemo(() => {
    const benchmarks: ProcessedBenchmark[] = [];

    for (const file of benchmarkFiles) {
      const runId = file.filename;
      const timestamp = parseTimestampFromTitle(file.data.Title) || file.timestamp;

      for (const benchmark of file.data.Benchmarks) {
        // Skip benchmarks with no statistics (failed runs)
        if (!benchmark.Statistics || !benchmark.Statistics.Mean || benchmark.Statistics.Mean === 0) {
          console.log(`Skipping benchmark with no data: ${benchmark.DisplayInfo}`);
          continue;
        }

        const category = CATEGORY_MAP[benchmark.Method] || 'Other';
        const implementation = detectImplementation(benchmark.Method, benchmark.DisplayInfo);
        const cleanName = extractCleanName(benchmark.DisplayInfo);

        benchmarks.push({
          name: cleanName,
          method: benchmark.Method,
          category,
          implementation,
          meanNs: benchmark.Statistics.Mean,
          meanUs: benchmark.Statistics.Mean / 1000,
          meanMs: benchmark.Statistics.Mean / 1000000,
          stdDev: benchmark.Statistics.StandardDeviation ?? 0,
          allocatedBytes: benchmark.Memory?.BytesAllocatedPerOperation ?? 0,
          allocatedKb: (benchmark.Memory?.BytesAllocatedPerOperation ?? 0) / 1024,
          gen0: benchmark.Memory?.Gen0Collections ?? 0,
          gen1: benchmark.Memory?.Gen1Collections ?? 0,
          gen2: benchmark.Memory?.Gen2Collections ?? 0,
          rank: benchmark.Rank,
          timestamp,
          runId,
        });
      }
    }

    console.log('Processed benchmarks:', benchmarks.length);
    console.log('Sample processed:', benchmarks[0]);
    return benchmarks;
  }, [benchmarkFiles]);

  const categories = useMemo(() => {
    const cats = new Set<string>();
    for (const b of processedData) {
      cats.add(b.category);
    }
    return ['All', ...Array.from(cats).sort()];
  }, [processedData]);

  const categoryData = useMemo(() => {
    const filteredBenchmarks =
      selectedCategory === 'All'
        ? processedData
        : processedData.filter((b) => b.category === selectedCategory);

    const categoryGroups = new Map<string, ProcessedBenchmark[]>();
    for (const b of filteredBenchmarks) {
      const existing = categoryGroups.get(b.category) || [];
      existing.push(b);
      categoryGroups.set(b.category, existing);
    }

    const result: CategoryData[] = [];
    for (const [name, benchmarks] of categoryGroups) {
      const chartDataMap = new Map<string, ChartDataPoint>();

      for (const b of benchmarks) {
        // Use runId as the unique key to avoid merging different runs on the same day
        const runKey = b.runId;
        const existing = chartDataMap.get(runKey) || {
          date: '', // Will be set after checking for duplicates
          timestamp: b.timestamp.getTime(),
          runId: b.runId,
        };

        if (b.implementation === 'Ignixa') {
          existing.ignixa = b.meanNs;
          existing.ignixaStdDev = b.stdDev;
          existing.ignixaAlloc = b.allocatedBytes;
        } else if (b.implementation === 'Firely') {
          existing.firely = b.meanNs;
          existing.firelyStdDev = b.stdDev;
          existing.firelyAlloc = b.allocatedBytes;
        } else if (b.implementation === 'Hybrid') {
          existing.hybrid = b.meanNs;
          existing.hybridStdDev = b.stdDev;
          existing.hybridAlloc = b.allocatedBytes;
        }

        chartDataMap.set(runKey, existing);
      }

      const chartData = Array.from(chartDataMap.values()).sort(
        (a, b) => a.timestamp - b.timestamp
      );

      // Check if multiple runs are on the same day - if so, include time in labels
      const dateCount = new Map<string, number>();
      for (const point of chartData) {
        const dateOnly = formatDate(new Date(point.timestamp));
        dateCount.set(dateOnly, (dateCount.get(dateOnly) || 0) + 1);
      }

      // Set date labels - use time if there are duplicates on same day
      for (const point of chartData) {
        const date = new Date(point.timestamp);
        const dateOnly = formatDate(date);
        point.date = dateCount.get(dateOnly)! > 1 ? formatDateWithTime(date) : dateOnly;
      }

      result.push({ name, benchmarks, chartData });
    }

    return result.sort((a, b) => a.name.localeCompare(b.name));
  }, [processedData, selectedCategory]);

  const latestComparison = useMemo(() => {
    if (benchmarkFiles.length === 0) {
      console.log('No benchmark files for comparison');
      return [];
    }

    // Use FhirPath benchmarks for the summary (it has Ignixa vs Firely comparisons)
    const latestFile = benchmarkFiles.find(f => f.filename.includes('FhirPathBenchmarks'))
      || benchmarkFiles[benchmarkFiles.length - 1];
    console.log('Latest file for comparison:', latestFile.filename);

    const comparisons: Array<{
      category: string;
      ignixaMethod: string;
      firelyMethod: string;
      hybridMethod?: string;
      ignixaTime: number;
      firelyTime: number;
      hybridTime?: number;
      speedup: number;
      ignixaAlloc: number;
      firelyAlloc: number;
      memoryRatio: number;
    }> = [];

    const byCategory = new Map<string, ProcessedBenchmark[]>();
    const latestBenchmarks = processedData.filter((b) => b.runId === latestFile.filename);
    console.log('Latest benchmarks for comparison:', latestBenchmarks.length);

    for (const b of latestBenchmarks) {
      const existing = byCategory.get(b.category) || [];
      existing.push(b);
      byCategory.set(b.category, existing);
    }

    console.log('Categories for comparison:', Array.from(byCategory.keys()));

    for (const [category, benchmarks] of byCategory) {
      const ignixa = benchmarks.find((b) => b.implementation === 'Ignixa');
      const firely = benchmarks.find((b) => b.implementation === 'Firely');
      const hybrid = benchmarks.find((b) => b.implementation === 'Hybrid');

      console.log(`Category ${category}: Ignixa=${!!ignixa}, Firely=${!!firely}, Hybrid=${!!hybrid}`);

      if (ignixa && firely) {
        comparisons.push({
          category,
          ignixaMethod: ignixa.method,
          firelyMethod: firely.method,
          hybridMethod: hybrid?.method,
          ignixaTime: ignixa.meanNs,
          firelyTime: firely.meanNs,
          hybridTime: hybrid?.meanNs,
          speedup: firely.meanNs / ignixa.meanNs,
          ignixaAlloc: ignixa.allocatedBytes,
          firelyAlloc: firely.allocatedBytes,
          memoryRatio: ignixa.allocatedBytes > 0 ? firely.allocatedBytes / ignixa.allocatedBytes : 0,
        });
      }
    }

    console.log('Comparisons generated:', comparisons.length, comparisons);
    return comparisons.sort((a, b) => b.speedup - a.speedup);
  }, [processedData, benchmarkFiles]);

  function extractBaseName(displayInfo: string): string {
    // First extract clean name from BenchmarkDotNet format
    const cleanName = extractCleanName(displayInfo);
    // Then remove any component prefix using generic pattern: "[component]:"
    // Handles: "Ignixa:", "Firely:", "Hybrid:", etc.
    return cleanName
      .replace(/^[^:]+:\s*/, '')
      .trim();
  }

  function extractOperationName(displayInfo: string): string {
    // Extract the operation name before the parentheses
    // "Ignixa: Access array element (JsonNode direct)" -> "Access array element"
    const baseName = extractBaseName(displayInfo);
    const match = baseName.match(/^([^(]+)/);
    return match ? match[1].trim() : baseName;
  }

  function extractVariantLabel(displayInfo: string): string {
    // Extract component + variant for chart legend
    // "Ignixa: Access array element (JsonNode direct)" -> "Ignixa (JsonNode direct)"
    // "Hybrid: Simple FHIRPath (Firely parse + Ignixa eval)" -> "Hybrid (Firely parse + Ignixa eval)"
    const cleanName = extractCleanName(displayInfo);

    // Extract component prefix before colon
    const componentMatch = cleanName.match(/^([^:]+):/);
    const component = componentMatch ? componentMatch[1].trim() : 'Other';

    // Extract variant in parentheses
    const variantMatch = cleanName.match(/\(([^)]+)\)/);
    const variant = variantMatch ? ` (${variantMatch[1]})` : '';

    return `${component}${variant}`;
  }

  function detectImplementationFromDisplayInfo(displayInfo: string): 'Ignixa' | 'Firely' | 'Other' {
    // First extract clean name from BenchmarkDotNet format
    const cleanName = extractCleanName(displayInfo);
    if (/^Ignixa:/i.test(cleanName)) return 'Ignixa';
    if (/^(Firely|Firely SDK):/i.test(cleanName)) return 'Firely';
    return 'Other';
  }

  const allBenchmarksGrouped = useMemo(() => {
    const groups: Record<string, {
      operationName: string;
      suite: string;
      chartData: ChartDataPoint[];
      variants: Set<string>;
    }> = {};

    benchmarkFiles.forEach((file) => {
      const timestamp = file.timestamp;
      const runId = file.filename;

      file.data.Benchmarks.forEach((b) => {
        // Skip benchmarks with no statistics (failed runs)
        if (!b.Statistics || !b.Statistics.Mean || b.Statistics.Mean === 0) {
          return;
        }

        if (selectedSuite !== 'All' && b.Type !== selectedSuite) return;

        const operationName = extractOperationName(b.DisplayInfo);
        const variantLabel = extractVariantLabel(b.DisplayInfo);
        const suite = b.Type;
        const key = `${suite}::${operationName}`;

        if (!groups[key]) {
          groups[key] = {
            operationName,
            suite,
            chartData: [],
            variants: new Set(),
          };
        }

        groups[key].variants.add(variantLabel);

        let dataPoint = groups[key].chartData.find((d) => d.runId === runId);
        if (!dataPoint) {
          dataPoint = {
            date: formatDate(timestamp),
            timestamp: timestamp.getTime(),
            runId,
          };
          groups[key].chartData.push(dataPoint);
        }

        // Store value and stdDev under the variant label
        (dataPoint as any)[variantLabel] = b.Statistics.Mean;
        (dataPoint as any)[`${variantLabel}_stdDev`] = b.Statistics.StandardDeviation ?? 0;
      });
    });

    Object.values(groups).forEach((group) => {
      group.chartData.sort((a, b) => a.timestamp - b.timestamp);
    });

    return Object.values(groups);
  }, [benchmarkFiles, selectedSuite]);

  const allBenchmarksSuites = useMemo(() => {
    if (benchmarkFiles.length === 0) return ['All'];

    const suites = new Set<string>();

    benchmarkFiles.forEach((file) => {
      file.data.Benchmarks.forEach((b) => {
        if (b.Type) {
          suites.add(b.Type);
        }
      });
    });

    return ['All', ...Array.from(suites).sort()];
  }, [benchmarkFiles]);


  if (loading) {
    return (
      <div className={styles.container}>
        <div className={styles.loading}>Loading benchmark data...</div>
      </div>
    );
  }

  if (error) {
    return (
      <div className={styles.container}>
        <div className={styles.error}>
          <h3>Error Loading Benchmarks</h3>
          <p>{error}</p>
          <p>Make sure benchmark JSON files exist in /static/benchmarks/</p>
        </div>
      </div>
    );
  }

  if (benchmarkFiles.length === 0) {
    return (
      <div className={styles.container}>
        <div className={styles.empty}>
          <h3>No Benchmark Data Available</h3>
          <p>Run the benchmark workflow to generate data.</p>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <header className={styles.header}>
        <h1>Performance Dashboard</h1>
        <p className={styles.subtitle}>
          Benchmark results from {benchmarkFiles.length} suite{benchmarkFiles.length !== 1 ? 's' : ''}
        </p>
      </header>

      <div className={styles.tabContainer}>
        <button
          className={activeTab === 'fhirpath' ? styles.tabActive : styles.tab}
          onClick={() => setActiveTab('fhirpath')}
        >
          FhirPath Performance
        </button>
        <button
          className={activeTab === 'all' ? styles.tabActive : styles.tab}
          onClick={() => setActiveTab('all')}
        >
          All Benchmarks
        </button>
      </div>

      {activeTab === 'fhirpath' && (
        <>
          <section className={styles.controls}>
        <div className={styles.filterGroup}>
          <label htmlFor="category-select">Category:</label>
          <select
            id="category-select"
            value={selectedCategory}
            onChange={(e) => setSelectedCategory(e.target.value)}
            className={styles.select}
          >
            {categories.map((cat) => (
              <option key={cat} value={cat}>
                {cat}
              </option>
            ))}
          </select>
        </div>
      </section>

      <section className={styles.summarySection}>
        <h2>Latest Run Summary</h2>
        <div className={styles.comparisonGrid}>
          {latestComparison.map((comp) => (
            <div key={comp.category} className={styles.comparisonCard}>
              <h3>{comp.category}</h3>
              <div className={styles.speedupBadge}>
                <span className={styles.speedupValue}>{formatMultiplier(comp.speedup)}</span>
                <span className={styles.speedupLabel}>faster</span>
              </div>
              <div className={styles.comparisonDetails}>
                <div className={styles.detailRow}>
                  <span className={styles.ignixa}>Ignixa:</span>
                  <span>{formatNanoseconds(comp.ignixaTime)}</span>
                </div>
                <div className={styles.detailRow}>
                  <span className={styles.firely}>Firely:</span>
                  <span>{formatNanoseconds(comp.firelyTime)}</span>
                </div>
              </div>
              <div className={styles.memoryComparison}>
                <div className={styles.detailRow}>
                  <span className={styles.ignixa}>Ignixa Alloc:</span>
                  <span>{formatBytes(comp.ignixaAlloc)}</span>
                </div>
                <div className={styles.detailRow}>
                  <span className={styles.firely}>Firely Alloc:</span>
                  <span>{formatBytes(comp.firelyAlloc)}</span>
                </div>
              </div>
            </div>
          ))}
        </div>
      </section>

      <section className={styles.chartsSection}>
        <h2>Performance Trends</h2>
        {categoryData.filter(cat => cat.name !== 'Other').map((cat) => {
          // Find max value to determine appropriate unit
          const maxValue = Math.max(
            ...cat.chartData.flatMap(d => [d.ignixa || 0, d.firely || 0, d.hybrid || 0])
          );
          const getYAxisFormatter = (max: number) => {
            if (max >= 1000000) {
              return (value: number) => `${(value / 1000000).toFixed(2)} ms`;
            } else if (max >= 1000) {
              return (value: number) => `${(value / 1000).toFixed(2)} μs`;
            }
            return (value: number) => `${value.toFixed(2)} ns`;
          };

          return (
            <div key={cat.name} className={styles.chartContainer}>
              <h3>{cat.name}</h3>
              {CATEGORY_DESCRIPTIONS[cat.name] && (
                <p className={styles.categoryDescription}>
                  {CATEGORY_DESCRIPTIONS[cat.name]}
                </p>
              )}
              <ResponsiveContainer width="100%" height={300}>
                <LineChart data={cat.chartData} margin={{ top: 20, right: 30, left: 20, bottom: 5 }}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="date" />
                  <YAxis
                    scale="log"
                    domain={['auto', 'auto']}
                    tickFormatter={getYAxisFormatter(maxValue)}
                    allowDataOverflow={false}
                  />
                  <Tooltip content={<CustomTooltip showMemory={false} />} />
                  <Legend />
                  <Line
                    type="monotone"
                    dataKey="ignixa"
                    stroke="#2196F3"
                    strokeWidth={2}
                    name="Ignixa"
                    dot={{ fill: '#2196F3', r: 4 }}
                    activeDot={{ r: 6 }}
                  />
                  <Line
                    type="monotone"
                    dataKey="firely"
                    stroke="#FF5722"
                    strokeWidth={2}
                    name="Firely"
                    dot={{ fill: '#FF5722', r: 4 }}
                    activeDot={{ r: 6 }}
                  />
                  <Line
                    type="monotone"
                    dataKey="hybrid"
                    stroke="#9C27B0"
                    strokeWidth={2}
                    name="Hybrid"
                    dot={{ fill: '#9C27B0', r: 4 }}
                    activeDot={{ r: 6 }}
                  />
                </LineChart>
              </ResponsiveContainer>
            </div>
          );
        })}
      </section>

      <section className={styles.comparisonSection}>
        <h2>Side-by-Side Comparison (Latest Run)</h2>
        {categoryData.length > 0 && (
          <div className={styles.barChartContainer}>
            <ResponsiveContainer width="100%" height={400}>
              <BarChart
                data={latestComparison}
                layout="vertical"
                margin={{ top: 20, right: 30, left: 120, bottom: 5 }}
              >
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis
                  type="number"
                  scale="log"
                  domain={['auto', 'auto']}
                  tickFormatter={(value) => formatNanoseconds(value)}
                />
                <YAxis type="category" dataKey="category" width={110} />
                <Tooltip
                  formatter={(value: number) => formatNanoseconds(value)}
                />
                <Legend />
                <Bar
                  dataKey="ignixaTime"
                  fill="#2196F3"
                  name="Ignixa"
                />
                <Bar
                  dataKey="hybridTime"
                  fill="#9C27B0"
                  name="Hybrid"
                />
                <Bar
                  dataKey="firelyTime"
                  fill="#FF5722"
                  name="Firely"
                />
              </BarChart>
            </ResponsiveContainer>
          </div>
        )}
      </section>

      <section className={styles.detailsSection}>
        <h2>Detailed Results</h2>
        <div className={styles.tableWrapper}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>Category</th>
                <th>Benchmark</th>
                <th>Mean Time</th>
                <th>Std Dev</th>
                <th>Memory</th>
                <th>Gen0</th>
                <th>Rank</th>
              </tr>
            </thead>
            <tbody>
              {processedData
                .filter((b) => {
                  // Use same file as comparison summary (FhirPath benchmarks)
                  const latestFile = benchmarkFiles.find(f => f.filename.includes('FhirPathBenchmarks'))
                    || benchmarkFiles[benchmarkFiles.length - 1];
                  return b.runId === latestFile?.filename;
                })
                .sort((a, b) => {
                  const catCompare = a.category.localeCompare(b.category);
                  if (catCompare !== 0) return catCompare;
                  return (a.rank ?? 99) - (b.rank ?? 99);
                })
                .map((b, idx) => (
                  <tr
                    key={`${b.method}-${idx}`}
                    className={b.implementation === 'Ignixa' ? styles.ignixaRow : styles.firelyRow}
                  >
                    <td>{b.category}</td>
                    <td>
                      <span className={styles.benchmarkName}>{b.name}</span>
                    </td>
                    <td>{formatNanoseconds(b.meanNs)}</td>
                    <td>{formatNanoseconds(b.stdDev)}</td>
                    <td>{formatBytes(b.allocatedBytes)}</td>
                    <td>{b.gen0.toFixed(4)}</td>
                    <td>
                      <span className={b.rank === 1 ? styles.rankFirst : styles.rank}>
                        #{b.rank ?? '-'}
                      </span>
                    </td>
                  </tr>
                ))}
            </tbody>
          </table>
        </div>
      </section>

      <footer className={styles.footer}>
        <p>
          Last updated:{' '}
          {benchmarkFiles.length > 0
            ? formatDate(new Date(Math.max(...benchmarkFiles.map(f => f.timestamp.getTime()))))
            : 'N/A'}
        </p>
        <p>
          Data from {benchmarkFiles.length} benchmark suite{benchmarkFiles.length !== 1 ? 's' : ''} •{' '}
          <a href="https://github.com/brendankowitz/ignixa-fhir/tree/main/bench/Ignixa.Benchmarks" target="_blank" rel="noopener noreferrer">
            View benchmark source code
          </a>
        </p>
      </footer>
        </>
      )}

      {activeTab === 'all' && (
        <>
          <section className={styles.controls}>
            <div className={styles.filterGroup}>
              <label htmlFor="suite-select"><strong>Suite:</strong></label>
              <select
                id="suite-select"
                value={selectedSuite}
                onChange={(e) => setSelectedSuite(e.target.value)}
                className={styles.select}
              >
                {allBenchmarksSuites.map((suite) => (
                  <option key={suite} value={suite}>
                    {suite}
                  </option>
                ))}
              </select>
            </div>
          </section>

          {allBenchmarksGrouped.length === 0 && (
            <section className={styles.empty}>
              <p>No benchmarks found for the selected suite.</p>
            </section>
          )}

          {allBenchmarksGrouped.map((group) => {
            // Define colors for different variants
            const variantColors = [
              '#2196F3', // Blue (Ignixa)
              '#FF5722', // Orange-red (Firely)
              '#4CAF50', // Green
              '#9C27B0', // Purple
              '#FF9800', // Orange
              '#00BCD4', // Cyan
              '#E91E63', // Pink
              '#795548', // Brown
            ];
            const variantsArray = Array.from(group.variants);

            return (
              <div key={`${group.suite}::${group.operationName}`} className={styles.chartSection}>
                <h3>{group.operationName}</h3>
                <p className={styles.chartSubtitle}>{group.suite}</p>
                <ResponsiveContainer width="100%" height={300}>
                  <LineChart data={group.chartData} margin={{ top: 20, right: 30, left: 20, bottom: 5 }}>
                    <CartesianGrid strokeDasharray="3 3" />
                    <XAxis dataKey="date" />
                    <YAxis
                      scale="log"
                      domain={['auto', 'auto']}
                      tickFormatter={(value) => formatNanoseconds(value)}
                      allowDataOverflow={false}
                    />
                    <Tooltip content={<CustomTooltip showMemory={false} />} />
                    <Legend />
                    {variantsArray.map((variant, idx) => (
                      group.chartData.some((d) => (d as any)[variant] !== undefined) && (
                        <Line
                          key={variant}
                          type="monotone"
                          dataKey={variant}
                          stroke={variantColors[idx % variantColors.length]}
                          name={variant}
                          strokeWidth={2}
                          dot={{ r: 4 }}
                        />
                      )
                    ))}
                  </LineChart>
                </ResponsiveContainer>
              </div>
            );
          })}

          <section className={styles.detailsSection}>
            <h2>Detailed Results</h2>
            <div className={styles.tableWrapper}>
              <table className={styles.table}>
                <thead>
                  <tr>
                    <th>Suite</th>
                    <th>Benchmark</th>
                    <th>Mean Time</th>
                    <th>Std Dev</th>
                    <th>Memory</th>
                    <th>Gen0</th>
                    <th>Rank</th>
                  </tr>
                </thead>
                <tbody>
                  {benchmarkFiles.flatMap(file =>
                    file.data.Benchmarks
                      .filter(b => selectedSuite === 'All' || b.Type === selectedSuite)
                      .map((b, idx) => {
                        const impl = detectImplementationFromDisplayInfo(b.DisplayInfo);
                        const cleanName = extractCleanName(b.DisplayInfo);
                        return (
                          <tr
                            key={`${file.filename}-${b.Method}-${idx}`}
                            className={impl === 'Ignixa' ? styles.ignixaRow : impl === 'Firely' ? styles.firelyRow : ''}
                          >
                            <td>{b.Type}</td>
                            <td>
                              <span className={styles.benchmarkName}>{cleanName}</span>
                            </td>
                            <td>{formatNanoseconds(b.Statistics?.Mean ?? 0)}</td>
                            <td>{formatNanoseconds(b.Statistics?.StandardDeviation ?? 0)}</td>
                            <td>{formatBytes(b.Memory?.BytesAllocatedPerOperation ?? 0)}</td>
                            <td>{(b.Memory?.Gen0Collections ?? 0).toFixed(4)}</td>
                            <td>
                              <span className={b.Rank === 1 ? styles.rankFirst : styles.rank}>
                                #{b.Rank ?? '-'}
                              </span>
                            </td>
                          </tr>
                        );
                      })
                  )}
                </tbody>
              </table>
            </div>
          </section>

          <footer className={styles.footer}>
            <p>
              Last updated:{' '}
              {benchmarkFiles.length > 0
                ? formatDate(new Date(Math.max(...benchmarkFiles.map(f => f.timestamp.getTime()))))
                : 'N/A'}
            </p>
            <p>
              Data from {benchmarkFiles.length} benchmark suite{benchmarkFiles.length !== 1 ? 's' : ''} •{' '}
              <a href="https://github.com/brendankowitz/ignixa-fhir/tree/main/bench/Ignixa.Benchmarks" target="_blank" rel="noopener noreferrer">
                View benchmark source code
              </a>
            </p>
          </footer>
        </>
      )}
    </div>
  );
}
