export type ConformanceStatus = 'pass' | 'fail' | 'skipped' | 'error' | string;

export interface CellError {
  assertion?: string;
  received?: string;
}

export interface ImplReportResult {
  id: string;
  file: string;
  status: ConformanceStatus;
  duration_ms: number;
  error?: CellError | null;
}

export interface ImplReport {
  impl: string;
  startedAt: string;
  duration_ms: number;
  results: ImplReportResult[];
}

export interface ResultSummary {
  pass: number;
  fail: number;
  skipped: number;
  total: number;
  passRate: number;
}

export interface GroupedModule {
  id: string;
  label: string;
  results: ImplReportResult[];
  summary: ResultSummary;
}
