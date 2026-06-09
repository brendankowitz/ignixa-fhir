namespace Ignixa.ConformanceMatrix.Cli.Reporting;

internal static class MatrixBuilder
{
    public static (Run Run, IndexEntry Index) MergeReports(
        IReadOnlyList<ImplReport> reports,
        string commit = "",
        string commitMessage = "",
        string branch = "",
        string repoUrl = "")
    {
        var startedAt = reports
            .Select(r => r.StartedAt)
            .DefaultIfEmpty(DateTimeOffset.UtcNow)
            .Min();

        var durationMs = WallClockMs(reports);
        var runId = FormatRunId(startedAt);

        var impls = reports
            .Select(r => r.Impl)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => new Impl(id, Cap(id)))
            .ToList();

        var modules = BuildModules(reports);
        var statuses = BuildStatuses(reports);

        var (pass, fail, skipped) = CountOutcomes(reports);

        var run = new Run
        {
            Meta = new RunMeta
            {
                Id = runId,
                StartedAt = startedAt,
                DurationMs = durationMs,
                Commit = commit,
                CommitMessage = commitMessage,
                Branch = branch,
                SuiteVersion = "",
                RepoUrl = repoUrl
            },
            Impls = impls,
            Modules = modules,
            Statuses = statuses
        };

        var index = new IndexEntry
        {
            Id = runId,
            StartedAt = startedAt,
            DurationMs = durationMs,
            Commit = commit,
            CommitMessage = commitMessage,
            Branch = branch,
            Impls = impls.Select(i => i.Id).ToList(),
            Pass = pass,
            Fail = fail,
            Skipped = skipped
        };

        return (run, index);
    }

    internal static bool IsPass(string status) => status == "pass";

    internal static bool IsSkipped(string status) => status == "skipped";

    internal static bool IsFail(string status) => !IsPass(status) && !IsSkipped(status);

    private static IReadOnlyList<Module> BuildModules(IReadOnlyList<ImplReport> reports)
    {
        var buckets = new Dictionary<string, Dictionary<string, (ModuleTest Test, int Order)>>(StringComparer.Ordinal);
        var order = 0;

        foreach (var report in reports)
        {
            foreach (var result in report.Results)
            {
                var modId = ModuleIdFromFile(result.File);
                var key = TestKey(result.File, result.Id);

                if (!buckets.TryGetValue(modId, out var bucket))
                    buckets[modId] = bucket = new Dictionary<string, (ModuleTest, int)>(StringComparer.Ordinal);

                if (!bucket.ContainsKey(key))
                {
                    bucket[key] = (new ModuleTest
                    {
                        Id = key,
                        Title = TitleFromId(result.Id),
                        FullName = result.Id,
                        File = result.File
                    }, order++);
                }
            }
        }

        return buckets
            .Select(kvp => new Module
            {
                Id = kvp.Key,
                Label = Cap(kvp.Key),
                Tests = kvp.Value.Values
                    .OrderBy(t => t.Test.File, StringComparer.Ordinal)
                    .ThenBy(t => t.Order)
                    .Select(t => t.Test)
                    .ToList()
            })
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, Cell>>> BuildStatuses(
        IReadOnlyList<ImplReport> reports)
    {
        var statuses = new Dictionary<string, Dictionary<string, Dictionary<string, Cell>>>(StringComparer.Ordinal);

        foreach (var report in reports)
        {
            foreach (var result in report.Results)
            {
                var modId = ModuleIdFromFile(result.File);
                var key = TestKey(result.File, result.Id);

                if (!statuses.TryGetValue(modId, out var byTest))
                    statuses[modId] = byTest = new Dictionary<string, Dictionary<string, Cell>>(StringComparer.Ordinal);

                if (!byTest.TryGetValue(key, out var byImpl))
                    byTest[key] = byImpl = new Dictionary<string, Cell>(StringComparer.Ordinal);

                byImpl[report.Impl] = new Cell
                {
                    Status = result.Status,
                    DurationMs = result.DurationMs,
                    Error = result.Error
                };
            }
        }

        return statuses;
    }

    internal static (int Pass, int Fail, int Skipped) CountOutcomes(IReadOnlyList<ImplReport> reports)
    {
        int pass = 0, fail = 0, skipped = 0;
        foreach (var report in reports)
        {
            foreach (var result in report.Results)
            {
                if (IsPass(result.Status)) pass++;
                else if (IsSkipped(result.Status)) skipped++;
                else fail++;
            }
        }
        return (pass, fail, skipped);
    }

    private static long WallClockMs(IReadOnlyList<ImplReport> reports)
    {
        if (reports.Count == 0)
            return 0;

        var start = DateTimeOffset.MaxValue;
        var end = DateTimeOffset.MinValue;

        foreach (var report in reports)
        {
            var s = report.StartedAt;
            var e = s.AddMilliseconds(report.DurationMs);
            if (s < start) start = s;
            if (e > end) end = e;
        }

        return (long)(end - start).TotalMilliseconds;
    }

    private static string FormatRunId(DateTimeOffset startedAt)
    {
        var utc = startedAt.UtcDateTime;
        return $"run-{utc:yyyy-MM-dd-HHmmss}";
    }

    internal static string ModuleIdFromFile(string file)
    {
        var normalized = file.Replace('\\', '/');
        return normalized.Split('/').FirstOrDefault() ?? file;
    }

    private static string TestKey(string file, string id) => $"{file}::{id}";

    private static string TitleFromId(string id)
    {
        var idx = id.LastIndexOf(" > ", StringComparison.Ordinal);
        return idx >= 0 ? id[(idx + 3)..] : id;
    }

    private static string Cap(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
