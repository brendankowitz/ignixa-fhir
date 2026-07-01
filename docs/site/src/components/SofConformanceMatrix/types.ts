export interface ImplementationManifestEntry {
  name: string;
  url: string;
  description: string;
  sourceUrl: string | null;
  localResultsPath: string | null;
}

export interface SuiteTest {
  title: string;
  tags: string[];
}

export interface Suite {
  file: string;
  title: string;
  tests: SuiteTest[];
}

export interface TestReportCase {
  name: string;
  result: {
    passed: boolean;
    error?: string;
  };
}

export interface TestReport {
  [suiteFile: string]: {
    tests: TestReportCase[];
  };
}

export interface ImplementationResults {
  entry: ImplementationManifestEntry;
  report: TestReport | null;
}
