import React, { useEffect, useMemo, useState } from 'react';
import useBaseUrl from '@docusaurus/useBaseUrl';
import type {
  ConformanceStep,
  GroupedModule,
  HttpTraceRequest,
  HttpTraceResponse,
  ImplReport,
  ImplReportResult,
  ResultSummary,
} from './types';
import styles from './styles.module.css';

async function fetchJson<T>(url: string): Promise<T> {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`);
  }

  return (await response.json()) as T;
}

function isPass(status: string): boolean {
  return status === 'pass';
}

function isSkipped(status: string): boolean {
  return status === 'skipped';
}

function summarize(results: ImplReportResult[]): ResultSummary {
  const pass = results.filter((result) => isPass(result.status)).length;
  const skipped = results.filter((result) => isSkipped(result.status)).length;
  const fail = results.length - pass - skipped;
  const total = results.length;
  const passRate = total === 0 ? 0 : Math.round((pass / total) * 1000) / 10;

  return { pass, fail, skipped, total, passRate };
}

function moduleIdFromFile(file: string): string {
  return file.split('/')[0] || file;
}

function titleFromId(id: string): string {
  const separator = ' > ';
  const index = id.lastIndexOf(separator);
  return index >= 0 ? id.slice(index + separator.length) : id;
}

function labelFromModuleId(id: string): string {
  return id.length === 0 ? id : `${id[0].toUpperCase()}${id.slice(1)}`;
}

function groupByModule(results: ImplReportResult[]): GroupedModule[] {
  const groups = new Map<string, ImplReportResult[]>();

  for (const result of results) {
    const id = moduleIdFromFile(result.file);
    const existing = groups.get(id) ?? [];
    existing.push(result);
    groups.set(id, existing);
  }

  return Array.from(groups.entries())
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([id, moduleResults]) => ({
      id,
      label: labelFromModuleId(id),
      results: moduleResults,
      summary: summarize(moduleResults),
    }));
}

function formatDuration(durationMs: number): string {
  if (durationMs < 1000) return `${durationMs}ms`;
  return `${(durationMs / 1000).toFixed(1)}s`;
}

function statusClass(status: string): string {
  if (isPass(status)) return styles.pass;
  if (isSkipped(status)) return styles.skipped;
  return styles.fail;
}

function statusLabel(status: string): string {
  if (isPass(status)) return 'Pass';
  if (isSkipped(status)) return 'Skipped';
  return status === 'error' ? 'Error' : 'Fail';
}

function stepTitle(step: ConformanceStep, index: number): string {
  return step.label ?? step.description ?? `${step.kind} ${index + 1}`;
}

function formatHeaders(headers: Record<string, string>): string {
  const entries = Object.entries(headers);
  if (entries.length === 0) return '(none)';
  return entries.map(([key, value]) => `${key}: ${value}`).join('\n');
}

function renderRequest(request: HttpTraceRequest): JSX.Element {
  return (
    <div className={styles.exchangePanel}>
      <h5>Request</h5>
      <code>
        {request.method} {request.url}
      </code>
      <pre>{formatHeaders(request.headers)}</pre>
      {request.body ? <pre>{request.body}</pre> : null}
    </div>
  );
}

function renderResponse(response: HttpTraceResponse): JSX.Element {
  return (
    <div className={styles.exchangePanel}>
      <h5>Response</h5>
      <code>Status {response.statusCode}</code>
      <pre>{formatHeaders(response.headers)}</pre>
      {response.bodyParseError ? <p className={styles.parseError}>{response.bodyParseError}</p> : null}
      {response.body ? <pre>{response.body}</pre> : null}
    </div>
  );
}

function renderDetails(result: ImplReportResult): JSX.Element {
  const steps = result.steps ?? [];
  if (!result.error && steps.length === 0) {
    return <span className={styles.noDetails}>-</span>;
  }

  return (
    <details className={styles.failureDetails}>
      <summary>
        {result.error?.assertion ?? `${steps.length} step${steps.length === 1 ? '' : 's'}`}
      </summary>
      {result.error ? <pre>{result.error.received ?? '(no details captured)'}</pre> : null}
      {steps.length > 0 ? (
        <div className={styles.stepTrace}>
          {steps.map((step, index) => (
            <article className={styles.stepCard} key={`${step.kind}:${step.label ?? index}:${index}`}>
              <header>
                <div>
                  <strong>{stepTitle(step, index)}</strong>
                  {step.description ? <span>{step.description}</span> : null}
                </div>
                <span className={`${styles.statusPill} ${statusClass(step.status)}`}>
                  {statusLabel(step.status)}
                </span>
              </header>
              <p>
                {step.phase} · {step.kind} · {formatDuration(step.duration_ms)}
                {step.message ? ` · ${step.message}` : ''}
              </p>
              {step.request ? renderRequest(step.request) : null}
              {step.response ? renderResponse(step.response) : null}
            </article>
          ))}
        </div>
      ) : null}
    </details>
  );
}

export default function FhirConformanceDashboard(): JSX.Element {
  const reportUrl = useBaseUrl('/conformance/latest.json');
  const [report, setReport] = useState<ImplReport | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    fetchJson<ImplReport>(reportUrl)
      .then((data) => {
        if (!cancelled) setReport(data);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Unknown error');
      });

    return () => {
      cancelled = true;
    };
  }, [reportUrl]);

  const summary = useMemo(() => summarize(report?.results ?? []), [report]);
  const modules = useMemo(() => groupByModule(report?.results ?? []), [report]);

  if (error) {
    return (
      <main className={styles.container}>
        <p className={styles.error}>FHIR conformance data could not be loaded: {error}</p>
      </main>
    );
  }

  if (!report) {
    return (
      <main className={styles.container}>
        <p>Loading FHIR conformance data...</p>
      </main>
    );
  }

  return (
    <main className={styles.container}>
      <section className={styles.hero}>
        <div>
          <p className={styles.eyebrow}>FHIR R4 TestScript conformance</p>
          <h1>Ignixa conformance report</h1>
          <p className={styles.subtitle}>
            Latest CI run against the repository's TestScript suite derived from fhir262.
          </p>
        </div>
        <dl className={styles.metaGrid}>
          <div>
            <dt>Implementation</dt>
            <dd>{report.impl}</dd>
          </div>
          <div>
            <dt>Started</dt>
            <dd>{new Date(report.startedAt).toLocaleString()}</dd>
          </div>
          <div>
            <dt>Duration</dt>
            <dd>{formatDuration(report.duration_ms)}</dd>
          </div>
          <div>
            <dt>FHIR version</dt>
            <dd>R4 / 4.0</dd>
          </div>
        </dl>
      </section>

      <section className={styles.summaryGrid} aria-label="Conformance result summary">
        <div className={styles.summaryCard}>
          <span className={styles.summaryValue}>{summary.passRate}%</span>
          <span className={styles.summaryLabel}>Pass rate</span>
        </div>
        <div className={styles.summaryCard}>
          <span className={styles.summaryValue}>{summary.pass}</span>
          <span className={styles.summaryLabel}>Passing</span>
        </div>
        <div className={styles.summaryCard}>
          <span className={styles.summaryValue}>{summary.fail}</span>
          <span className={styles.summaryLabel}>Failing</span>
        </div>
        <div className={styles.summaryCard}>
          <span className={styles.summaryValue}>{summary.skipped}</span>
          <span className={styles.summaryLabel}>Skipped</span>
        </div>
      </section>

      <section className={styles.modules}>
        {modules.map((module) => (
          <article className={styles.moduleCard} key={module.id}>
            <header className={styles.moduleHeader}>
              <div>
                <h2>{module.label}</h2>
                <p>{module.summary.total} tests</p>
              </div>
              <div className={styles.moduleCounts}>
                <span className={styles.pass}>{module.summary.pass} pass</span>
                <span className={styles.fail}>{module.summary.fail} fail</span>
                <span className={styles.skipped}>{module.summary.skipped} skipped</span>
              </div>
            </header>
            <div className={styles.tableWrap}>
              <table className={styles.resultsTable}>
                <thead>
                  <tr>
                    <th>Test</th>
                    <th>Status</th>
                    <th>Duration</th>
                    <th>Details</th>
                  </tr>
                </thead>
                <tbody>
                  {module.results.map((result) => (
                    <tr key={`${result.file}:${result.id}`}>
                      <td>
                        <strong>{titleFromId(result.id)}</strong>
                        <span>{result.file}</span>
                      </td>
                      <td>
                        <span className={`${styles.statusPill} ${statusClass(result.status)}`}>
                          {statusLabel(result.status)}
                        </span>
                      </td>
                      <td>{formatDuration(result.duration_ms)}</td>
                      <td>{renderDetails(result)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </article>
        ))}
      </section>
    </main>
  );
}
