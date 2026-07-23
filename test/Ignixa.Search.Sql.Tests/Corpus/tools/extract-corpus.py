"""Extracts the slim legacy-SQL corpus consumed by the differential tests.

Input is an ignixa-sql-capture artifact directory (the TestScript run that recorded, per HTTP
request, the SQL the shipping search engine actually executed). Output is
``../legacy-sql-corpus.json`` -- one entry per distinct search URL.

Correlation in the capture is timestamp-windowed, so a single request can pick up SQL text that
belongs to an adjacent request. Every SQL event correlated to a request carries the *batch* text,
so the genuine query appears on several events of the same request while a leaked neighbour
appears on one. The modal SQL text per URL is therefore the request's own query; entries whose
mode is not corroborated by at least two events are marked low-confidence and excluded by default.

Usage:
    python extract-corpus.py <capture-artifact-dir> [--include-low-confidence]
"""

import argparse
import collections
import csv
import json
import os
import re
import sys

CSV_NAME = "passing-test-sql.csv"
OUTPUT_NAME = "legacy-sql-corpus.json"

DECLARE_RE = re.compile(r"^DECLARE (@p\d+) (?:AS )?([A-Za-z]+(?:\([\w, ]+\))?) = (.*)$")
DECLARATION_RE = re.compile(r"(@p\d+) ([A-Za-z]+(?:\([\w, ]+\))?)")


def split_batch_prefix(sql):
    """Splits `(@p0 varchar(256),@p1 int)<body>` into (declarations, body).

    The prefix cannot be matched with a naive `\\([^)]*\\)` because parameter types carry their own
    parentheses (`varchar(256)`, `decimal(18,6)`); depth counting is what actually terminates it.
    """
    if not sql.startswith("(@p"):
        return "", sql
    depth = 0
    for index, character in enumerate(sql):
        if character == "(":
            depth += 1
        elif character == ")":
            depth -= 1
            if depth == 0:
                return sql[1:index], sql[index + 1:]
    return "", sql


def read_rows(capture_dir):
    path = os.path.join(capture_dir, CSV_NAME)
    if not os.path.exists(path):
        sys.exit(f"not a capture artifact directory (no {CSV_NAME}): {capture_dir}")
    csv.field_size_limit(10**9)
    with open(path, encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def is_search_batch(sql):
    return sql.startswith("(@p") and ";WITH" in sql


def unwrap_log_event(sql):
    """Returns the inner SQL of a `exec dbo.LogEvent ... @Text=N'<sql>'` wrapper, or None."""
    if not sql.startswith("exec dbo.LogEvent"):
        return None
    marker = "@Text=N'"
    start = sql.find(marker)
    if start < 0:
        return None
    return sql[start + len(marker):].rstrip("'").replace("''", "'")


def parameter_values(log_event_sql):
    """Pulls the `DECLARE @p0 varchar(64) = 'value'` preamble the LogEvent variant carries."""
    values = {}
    for line in log_event_sql.splitlines():
        match = DECLARE_RE.match(line.strip())
        if not match:
            continue
        name, sql_type, literal = match.groups()
        literal = literal.strip()
        if literal.startswith("'") and literal.endswith("'"):
            literal = literal[1:-1]
        values[name] = {"type": sql_type, "value": literal}
    return values


def parameter_types(sql):
    declarations, _ = split_batch_prefix(sql)
    return {name: sql_type for name, sql_type in DECLARATION_RE.findall(declarations)}


def strip_batch_prefix(sql):
    """Drops the sp_executesql parameter prefix and the SET STATISTICS preamble."""
    _, body = split_batch_prefix(sql)
    lines = [
        line for line in body.splitlines()
        if not line.startswith("SET STATISTICS")
    ]
    return "\n".join(lines).strip()


def build_entries(rows, include_low_confidence):
    batches = collections.defaultdict(collections.Counter)
    log_events = {}
    provenance = collections.defaultdict(set)

    for row in rows:
        if row["Method"] != "GET" or not row["SearchQuery"]:
            continue
        url, sql = row["RequestUrl"], row["RawSql"]
        if is_search_batch(sql):
            batches[url][sql] += 1
            provenance[url].add((row["ScriptRelativePath"], row["TestName"]))
            continue
        inner = unwrap_log_event(sql)
        if inner and ";WITH" in inner:
            log_events.setdefault(url, inner)

    entries = []
    for url, counter in sorted(batches.items()):
        sql, occurrences = counter.most_common(1)[0]
        confident = occurrences >= 2
        if not confident and not include_low_confidence:
            continue
        scripts = sorted({script for script, _ in provenance[url]})
        entries.append({
            "url": url,
            "legacySql": strip_batch_prefix(sql),
            "parameterTypes": parameter_types(sql),
            "parameterValues": parameter_values(log_events.get(url, "")),
            "sourceScripts": scripts,
            "corroboratingEvents": occurrences,
            "rejectedVariants": len(counter) - 1,
        })
    return entries


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture_dir")
    parser.add_argument("--include-low-confidence", action="store_true")
    args = parser.parse_args()

    rows = read_rows(args.capture_dir)
    entries = build_entries(rows, args.include_low_confidence)

    output = {
        "captureRunId": rows[0]["RunId"] if rows else None,
        "entryCount": len(entries),
        "entries": entries,
    }
    destination = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", OUTPUT_NAME)
    with open(os.path.normpath(destination), "w", encoding="utf-8", newline="\n") as handle:
        json.dump(output, handle, indent=1)
        handle.write("\n")

    print(f"wrote {len(entries)} entries to {os.path.normpath(destination)}")


if __name__ == "__main__":
    main()
