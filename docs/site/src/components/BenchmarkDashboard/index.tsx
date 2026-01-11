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
  files: Array<{
    filename: string;
    timestamp: string;
    runNumber: number;
    commit: string;
    branch: string;
  }>;
  lastUpdated: string;
}

const CATEGORY_MAP: Record<string, string> = {
  IgnixaParseBaseline: 'Compilation',
  IgnixaParseOptimized: 'Compilation',
  FirelyCompile: 'Compilation',
  IgnixaSimple: 'Execution-Simple',
  FirelySimple: 'Execution-Simple',
  IgnixaArray: 'Execution-Array',
  FirelyArray: 'Execution-Array',
  IgnixaComplex: 'Execution-Complex',
  FirelyComplex: 'Execution-Complex',
  IgnixaSearchParam: 'Execution-SearchParam',
  FirelySearchParam: 'Execution-SearchParam',
  IgnixaScalar: 'Execution-Scalar',
  FirelyScalar: 'Execution-Scalar',
};

function detectImplementation(method: string, displayInfo: string): 'Ignixa' | 'Firely' {
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

function parseTimestampFromTitle(title: string): Date {
  const match = title.match(/(\d{8})/);
  if (match) {
    const dateStr = match[1];
    const year = parseInt(dateStr.slice(0, 4), 10);
    const month = parseInt(dateStr.slice(4, 6), 10) - 1;
    const day = parseInt(dateStr.slice(6, 8), 10);
    return new Date(year, month, day);
  }
  return new Date();
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
      {payload.map((entry, index) => (
        <p key={index} style={{ color: entry.color }} className={styles.tooltipEntry}>
          {entry.name}: {showMemory ? formatBytes(entry.value as number) : formatNanoseconds(entry.value as number)}
        </p>
      ))}
    </div>
  );
}

export default function BenchmarkDashboard(): JSX.Element {
  const [benchmarkFiles, setBenchmarkFiles] = useState<BenchmarkFile[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedCategory, setSelectedCategory] = useState<string>('All');
  const [showMemory, setShowMemory] = useState(false);
  const baseUrl = useBaseUrl('/');

  useEffect(() => {
    async function loadBenchmarks() {
      try {
        const metadataResponse = await fetch(`${baseUrl}benchmarks/latest.json`);
        if (!metadataResponse.ok) {
          throw new Error('Failed to load benchmark metadata');
        }
        const metadata: LatestMetadata = await metadataResponse.json();

        const files: BenchmarkFile[] = await Promise.all(
          metadata.files.map(async (fileInfo) => {
            const response = await fetch(`${baseUrl}benchmarks/${fileInfo.filename}`);
            if (!response.ok) {
              throw new Error(`Failed to load ${fileInfo.filename}`);
            }
            const data: BenchmarkRun = await response.json();
            return {
              filename: fileInfo.filename,
              timestamp: new Date(fileInfo.timestamp),
              data,
            };
          })
        );

        files.sort((a, b) => a.timestamp.getTime() - b.timestamp.getTime());
        setBenchmarkFiles(files);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Unknown error');
      } finally {
        setLoading(false);
      }
    }

    loadBenchmarks();
  }, []);

  const processedData = useMemo(() => {
    const benchmarks: ProcessedBenchmark[] = [];

    for (const file of benchmarkFiles) {
      const runId = file.filename;
      const timestamp = parseTimestampFromTitle(file.data.Title) || file.timestamp;

      for (const benchmark of file.data.Benchmarks) {
        const category = CATEGORY_MAP[benchmark.Method] || 'Other';
        const implementation = detectImplementation(benchmark.Method, benchmark.DisplayInfo);

        benchmarks.push({
          name: benchmark.DisplayInfo,
          method: benchmark.Method,
          category,
          implementation,
          meanNs: benchmark.Statistics.Mean,
          meanUs: benchmark.Statistics.Mean / 1000,
          meanMs: benchmark.Statistics.Mean / 1000000,
          stdDev: benchmark.Statistics.StdDev,
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
        const dateKey = formatDate(b.timestamp);
        const existing = chartDataMap.get(dateKey) || {
          date: dateKey,
          timestamp: b.timestamp.getTime(),
          runId: b.runId,
        };

        if (b.implementation === 'Ignixa') {
          existing.ignixa = b.meanNs;
          existing.ignixaAlloc = b.allocatedBytes;
        } else {
          existing.firely = b.meanNs;
          existing.firelyAlloc = b.allocatedBytes;
        }

        chartDataMap.set(dateKey, existing);
      }

      const chartData = Array.from(chartDataMap.values()).sort(
        (a, b) => a.timestamp - b.timestamp
      );

      result.push({ name, benchmarks, chartData });
    }

    return result.sort((a, b) => a.name.localeCompare(b.name));
  }, [processedData, selectedCategory]);

  const latestComparison = useMemo(() => {
    if (benchmarkFiles.length === 0) return [];

    const latestFile = benchmarkFiles[benchmarkFiles.length - 1];
    const comparisons: Array<{
      category: string;
      ignixaMethod: string;
      firelyMethod: string;
      ignixaTime: number;
      firelyTime: number;
      speedup: number;
      ignixaAlloc: number;
      firelyAlloc: number;
      memoryRatio: number;
    }> = [];

    const byCategory = new Map<string, ProcessedBenchmark[]>();
    for (const b of processedData.filter((b) => b.runId === latestFile.filename)) {
      const existing = byCategory.get(b.category) || [];
      existing.push(b);
      byCategory.set(b.category, existing);
    }

    for (const [category, benchmarks] of byCategory) {
      const ignixa = benchmarks.find((b) => b.implementation === 'Ignixa');
      const firely = benchmarks.find((b) => b.implementation === 'Firely');

      if (ignixa && firely) {
        comparisons.push({
          category,
          ignixaMethod: ignixa.method,
          firelyMethod: firely.method,
          ignixaTime: ignixa.meanNs,
          firelyTime: firely.meanNs,
          speedup: firely.meanNs / ignixa.meanNs,
          ignixaAlloc: ignixa.allocatedBytes,
          firelyAlloc: firely.allocatedBytes,
          memoryRatio: ignixa.allocatedBytes > 0 ? firely.allocatedBytes / ignixa.allocatedBytes : 0,
        });
      }
    }

    return comparisons.sort((a, b) => b.speedup - a.speedup);
  }, [processedData, benchmarkFiles]);

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
        <h1>FHIRPath Performance Dashboard</h1>
        <p className={styles.subtitle}>
          Comparing Ignixa vs Firely SDK performance across {benchmarkFiles.length} benchmark runs
        </p>
      </header>

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
        <div className={styles.toggleGroup}>
          <label className={styles.toggle}>
            <input
              type="checkbox"
              checked={showMemory}
              onChange={(e) => setShowMemory(e.target.checked)}
            />
            <span>Show Memory Allocation</span>
          </label>
        </div>
      </section>

      <section className={styles.summarySection}>
        <h2>Latest Run Summary</h2>
        <div className={styles.comparisonGrid}>
          {latestComparison.map((comp) => (
            <div key={comp.category} className={styles.comparisonCard}>
              <h3>{comp.category}</h3>
              <div className={styles.speedupBadge}>
                <span className={styles.speedupValue}>{comp.speedup.toFixed(1)}x</span>
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
        {categoryData.map((cat) => (
          <div key={cat.name} className={styles.chartContainer}>
            <h3>{cat.name}</h3>
            <ResponsiveContainer width="100%" height={300}>
              <LineChart data={cat.chartData} margin={{ top: 20, right: 30, left: 20, bottom: 5 }}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="date" />
                <YAxis
                  tickFormatter={(value) =>
                    showMemory ? formatBytes(value) : formatNanoseconds(value)
                  }
                />
                <Tooltip content={<CustomTooltip showMemory={showMemory} />} />
                <Legend />
                <Line
                  type="monotone"
                  dataKey={showMemory ? 'ignixaAlloc' : 'ignixa'}
                  stroke="#2196F3"
                  strokeWidth={2}
                  name="Ignixa"
                  dot={{ fill: '#2196F3', r: 4 }}
                  activeDot={{ r: 6 }}
                />
                <Line
                  type="monotone"
                  dataKey={showMemory ? 'firelyAlloc' : 'firely'}
                  stroke="#FF5722"
                  strokeWidth={2}
                  name="Firely"
                  dot={{ fill: '#FF5722', r: 4 }}
                  activeDot={{ r: 6 }}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        ))}
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
                  tickFormatter={(value) =>
                    showMemory ? formatBytes(value) : formatNanoseconds(value)
                  }
                />
                <YAxis type="category" dataKey="category" width={110} />
                <Tooltip
                  formatter={(value: number) =>
                    showMemory ? formatBytes(value) : formatNanoseconds(value)
                  }
                />
                <Legend />
                <Bar
                  dataKey={showMemory ? 'ignixaAlloc' : 'ignixaTime'}
                  fill="#2196F3"
                  name="Ignixa"
                />
                <Bar
                  dataKey={showMemory ? 'firelyAlloc' : 'firelyTime'}
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
                .filter((b) => b.runId === benchmarkFiles[benchmarkFiles.length - 1]?.filename)
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
            ? formatDate(benchmarkFiles[benchmarkFiles.length - 1].timestamp)
            : 'N/A'}
        </p>
        <p>
          Data from {benchmarkFiles.length} benchmark run{benchmarkFiles.length !== 1 ? 's' : ''}
        </p>
      </footer>
    </div>
  );
}
