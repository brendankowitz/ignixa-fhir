export type ConformanceStatus = 'pass' | 'fail' | 'skipped' | 'error' | string;

export interface CellError {
  assertion?: string;
  received?: string;
}

export interface HttpTraceRequest {
  method: string;
  url: string;
  headers: Record<string, string>;
  body?: string | null;
}

export interface HttpTraceResponse {
  statusCode: number;
  headers: Record<string, string>;
  body?: string | null;
  bodyParseError?: string | null;
}

export interface ConformanceStep {
  phase: 'setup' | 'test' | 'teardown' | string;
  kind: 'operation' | 'assertion' | string;
  label?: string | null;
  description?: string | null;
  status: ConformanceStatus;
  duration_ms: number;
  message?: string | null;
  request?: HttpTraceRequest | null;
  response?: HttpTraceResponse | null;
}

export interface ImplReportResult {
  id: string;
  file: string;
  suite?: string;
  category?: string;
  status: ConformanceStatus;
  duration_ms: number;
  error?: CellError | null;
  steps?: ConformanceStep[];
}

export interface ImplReport {
  impl: string;
  target?: string;
  fhirVersion?: string;
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
