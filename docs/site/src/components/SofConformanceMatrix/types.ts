export interface ImplementationManifestEntry {
  readonly name: string;
  readonly url: string;
  readonly description: string;
  readonly sourceUrl: string | null;
  readonly localResultsPath: string | null;
}

export interface SuiteTest {
  readonly title: string;
  readonly tags: readonly string[];
}

export interface Suite {
  readonly file: string;
  readonly title: string;
  readonly tests: readonly SuiteTest[];
}

// Third-party report format (produced by each implementation's own tooling, not validated at
// runtime) — fields below the top level aren't guaranteed, so `result` is optional to match how
// callers already treat it defensively.
export interface TestReportCase {
  readonly name: string;
  readonly result?: {
    readonly passed: boolean;
    readonly error?: string;
  };
}

export interface TestReport {
  readonly [suiteFile: string]: {
    readonly tests: readonly TestReportCase[];
  };
}

export interface ImplementationResults {
  readonly entry: ImplementationManifestEntry;
  readonly report: TestReport | null;
  // True when a report was expected (localResultsPath was set) but the client-side fetch
  // failed — distinct from a vendor never having published results at all.
  readonly reportFetchFailed: boolean;
}
