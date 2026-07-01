import React, { useEffect, useMemo, useState } from 'react';
import useBaseUrl from '@docusaurus/useBaseUrl';
import type {
  ImplementationManifestEntry,
  ImplementationResults,
  Suite,
  TestReport,
} from './types';
import styles from './styles.module.css';

const PINNED_IMPLEMENTATION = 'Ignixa';

const RESERVED_TAGS: Record<string, string> = {
  shareable: 'Profile: Shareable View Definition',
  tabular: 'Profile: Tabular View Definition',
  experimental: 'Experimental',
};

async function fetchJson<T>(url: string, context: string): Promise<T | null> {
  try {
    const response = await fetch(url);
    if (!response.ok) {
      console.error(`SofConformanceMatrix: ${context} fetch from ${url} returned ${response.status}`);
      return null;
    }
    return (await response.json()) as T;
  } catch (err) {
    console.error(`SofConformanceMatrix: ${context} fetch from ${url} failed`, err);
    return null;
  }
}

function passedFor(report: TestReport | null, suiteFile: string, testTitle: string): boolean | undefined {
  const cases = report?.[suiteFile]?.tests;
  return cases?.find((c) => c.name === testTitle)?.result?.passed;
}

function implementationTooltip(entry: ImplementationManifestEntry): string {
  return entry.sourceUrl ? `${entry.description} (mirrored from ${entry.sourceUrl})` : entry.description;
}

export default function SofConformanceMatrix(): JSX.Element {
  const manifestUrl = useBaseUrl('/sof-conformance/manifest.json');
  const testsUrl = useBaseUrl('/sof-conformance/tests.json');
  const basePath = useBaseUrl('/').replace(/\/$/, '');

  const [suites, setSuites] = useState<Suite[] | null>(null);
  const [results, setResults] = useState<ImplementationResults[] | null>(null);
  const [loadError, setLoadError] = useState(false);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      const [manifest, suiteData] = await Promise.all([
        fetchJson<ImplementationManifestEntry[]>(manifestUrl, 'manifest'),
        fetchJson<Suite[]>(testsUrl, 'suite index'),
      ]);

      if (!manifest || !suiteData) {
        if (!cancelled) setLoadError(true);
        return;
      }

      const withReports = await Promise.all(
        manifest.map(async (entry) => {
          const report = entry.localResultsPath
            ? await fetchJson<TestReport>(`${basePath}${entry.localResultsPath}`, `${entry.name} report`)
            : null;
          return {
            entry,
            report,
            // A report is expected whenever localResultsPath is set (it was mirrored
            // successfully at build time); null here means the fetch failed at page-view
            // time, which is a regression to surface — not the same as a vendor never
            // having published results in the first place.
            reportFetchFailed: Boolean(entry.localResultsPath) && report === null,
          };
        })
      );
      withReports.sort((a, b) => {
        if (a.entry.name === PINNED_IMPLEMENTATION) return -1;
        if (b.entry.name === PINNED_IMPLEMENTATION) return 1;
        return 0;
      });

      if (!cancelled) {
        setSuites(suiteData);
        setResults(withReports);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [manifestUrl, testsUrl, basePath]);

  const sections = useMemo(() => {
    if (!suites) return [];
    const tags = new Set<string>();
    suites.forEach((suite) => suite.tests.forEach((t) => t.tags.forEach((tag) => tags.add(tag))));

    const otherTags = Array.from(tags)
      .filter((tag) => !Object.keys(RESERVED_TAGS).includes(tag))
      .sort()
      .reduce<Record<string, string>>((acc, tag) => {
        acc[tag] = tag;
        return acc;
      }, {});

    return Object.entries({ ...RESERVED_TAGS, ...otherTags }).filter(([tag]) => tags.has(tag));
  }, [suites]);

  if (loadError) {
    return (
      <div className={styles.container}>
        <p className={styles.error}>Conformance data could not be loaded for this deployment.</p>
      </div>
    );
  }

  if (!suites || !results) {
    return (
      <div className={styles.container}>
        <p>Loading conformance data...</p>
      </div>
    );
  }

  return (
    <div className={styles.container}>
      <div className={styles.scroller}>
      <table className={styles.matrix}>
        <thead>
          <tr>
            <th className={styles.testHeader}>Test</th>
            {results.map(({ entry }) => (
              <th
                key={entry.name}
                className={
                  entry.name === PINNED_IMPLEMENTATION
                    ? `${styles.implHeader} ${styles.pinnedHeader}`
                    : styles.implHeader
                }
              >
                <a href={entry.url} title={implementationTooltip(entry)}>
                  {entry.name}
                </a>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {sections.map(([tag, label]) => (
            <React.Fragment key={tag}>
              <tr>
                <td className={styles.sectionRow} colSpan={results.length + 1}>
                  {label}
                </td>
              </tr>
              {suites
                .filter((suite) => suite.tests.some((t) => t.tags.includes(tag)))
                .map((suite) => (
                  <React.Fragment key={suite.file}>
                    <tr>
                      <td className={styles.suiteRow} colSpan={results.length + 1}>
                        {suite.title}
                      </td>
                    </tr>
                    {suite.tests
                      .filter((t) => t.tags.includes(tag))
                      .map((test) => (
                        <tr key={`${suite.file}:${test.title}`}>
                          <td className={styles.testCell}>{test.title}</td>
                          {results.map(({ entry, report, reportFetchFailed }) => {
                            const passed = passedFor(report, suite.file, test.title);
                            const cellClass =
                              entry.name === PINNED_IMPLEMENTATION
                                ? `${styles.resultCell} ${styles.pinnedCell}`
                                : styles.resultCell;
                            return (
                              <td key={entry.name} className={cellClass}>
                                {passed === true && <span className={styles.pass}>&#10003;</span>}
                                {passed === false && <span className={styles.fail}>&#9888;</span>}
                                {passed === undefined && reportFetchFailed && (
                                  <span className={styles.fetchFailed} title="Results unavailable — fetch failed">
                                    ?
                                  </span>
                                )}
                                {passed === undefined && !reportFetchFailed && (
                                  <span className={styles.noData}>&minus;</span>
                                )}
                              </td>
                            );
                          })}
                        </tr>
                      ))}
                  </React.Fragment>
                ))}
            </React.Fragment>
          ))}
        </tbody>
      </table>
      </div>
    </div>
  );
}
