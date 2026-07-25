using System.IO;
using System.Text;

namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>
/// Runs every captured real-world search through the compiler and compares what it asks the database
/// for against what the shipping engine asked for. The point is triage, not parity: a divergence is a
/// question to answer (a feature the compiler lacks, a table read the compiler avoids, or a filter the
/// shipping engine applies for a reason worth knowing), so the suite writes a report and guards only
/// the counts that must not regress.
/// </summary>
public class LegacyCorpusDifferentialTests
{
    [Fact]
    public void GivenTheCapturedCorpus_WhenLoaded_ThenEveryEntryCarriesAQueryAndItsLegacySql()
    {
        var entries = LegacyCorpus.Entries;

        entries.ShouldNotBeEmpty();
        entries.ShouldAllBe(e => e.QueryString.Length > 0);
        entries.ShouldAllBe(e => e.LegacySql.Contains(";WITH", StringComparison.Ordinal));
        entries.ShouldAllBe(e => e.CorroboratingEvents >= 2);
    }

    [Fact]
    public void GivenTheCapturedCorpus_WhenEachLegacyQueryIsCanonicalized_ThenItParsesAsTSql()
    {
        var unparseable = new List<string>();

        foreach (var entry in LegacyCorpus.Entries)
        {
            try
            {
                SqlShapeCanonicalizer.Canonicalize(entry.LegacySql);
            }
            catch (FormatException exception)
            {
                unparseable.Add($"{entry.Url}: {exception.Message}");
            }
        }

        unparseable.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenTheCapturedCorpus_WhenComparedAgainstTheCompiler_ThenTheDifferentialReportIsWritten()
    {
        var results = await RunAsync();

        var report = DifferentialReport.Render(results);
        var path = Path.Combine(AppContext.BaseDirectory, "legacy-sql-differential-report.md");
        await File.WriteAllTextAsync(path, report, Encoding.UTF8);

        report.ShouldContain("## Summary");
    }

    [Fact]
    public async Task GivenTheCapturedCorpus_WhenCompiled_ThenNoFewerQueriesCompileThanTheRecordedBaseline()
    {
        var results = await RunAsync();
        var compiled = results.Count(r => r.Compilation.Succeeded);

        // Raise this with the compiler. Never lower it without recording why in the report's Gaps section.
        compiled.ShouldBeGreaterThanOrEqualTo(DifferentialBaseline.CompiledQueries);
    }

    private static async Task<IReadOnlyList<DifferentialResult>> RunAsync()
    {
        var results = new List<DifferentialResult>();

        foreach (var entry in LegacyCorpus.Entries)
        {
            var compilation = await CorpusCompiler.CompileAsync(entry);
            var legacy = SqlShapeCanonicalizer.Canonicalize(entry.LegacySql);

            if (!compilation.Succeeded)
            {
                results.Add(new DifferentialResult(entry, compilation, legacy, null, null));
                continue;
            }

            var compiled = SqlShapeCanonicalizer.Canonicalize(compilation.Sql!);
            results.Add(new DifferentialResult(entry, compilation, legacy, compiled, ShapeComparison.Compare(legacy, compiled)));
        }

        return results;
    }
}
