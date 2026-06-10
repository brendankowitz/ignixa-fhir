using System.CommandLine;
using System.Text.Json;
using Ignixa.ConformanceMatrix.Cli.Reporting;

namespace Ignixa.ConformanceMatrix.Cli.Commands;

internal static class MergeCommand
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static Command Build()
    {
        var command = new Command("merge", "Merge per-impl reports into a conformance matrix run and update the index");

        var resultsOption = new Option<string>("--results") { Description = "Directory containing per-impl report JSON files", Required = true };
        var outOption = new Option<string>("--out") { Description = "Output directory for runs/ and index.json", Required = true };
        var commitOption = new Option<string>("--commit") { Description = "Git commit SHA", DefaultValueFactory = _ => "" };
        var branchOption = new Option<string>("--branch") { Description = "Git branch name", DefaultValueFactory = _ => "" };
        var commitMessageOption = new Option<string>("--commit-message") { Description = "Git commit message", DefaultValueFactory = _ => "" };
        var repoUrlOption = new Option<string>("--repo-url") { Description = "Repository URL", DefaultValueFactory = _ => "" };

        command.Options.Add(resultsOption);
        command.Options.Add(outOption);
        command.Options.Add(commitOption);
        command.Options.Add(branchOption);
        command.Options.Add(commitMessageOption);
        command.Options.Add(repoUrlOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var results = parseResult.GetValue(resultsOption)!;
            var outDir = parseResult.GetValue(outOption)!;
            var commit = parseResult.GetValue(commitOption)!;
            var branch = parseResult.GetValue(branchOption)!;
            var commitMessage = parseResult.GetValue(commitMessageOption)!;
            var repoUrl = parseResult.GetValue(repoUrlOption)!;
            return MergeAsync(results, outDir, commit, commitMessage, branch, repoUrl, cancellationToken);
        });

        return command;
    }

    private static async Task<int> MergeAsync(
        string resultsDir, string outDir, string commit, string commitMessage, string branch, string repoUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(resultsDir))
            {
                Console.Error.WriteLine($"error: --results directory not found: {resultsDir}");
                return 1;
            }

            var reports = new List<ImplReport>();
            var badFiles = new List<string>();

            foreach (var file in Directory.EnumerateFiles(resultsDir, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken);
                    var report = JsonSerializer.Deserialize<ImplReport>(json);
                    if (report is not null)
                        reports.Add(report);
                    else
                    {
                        Console.Error.WriteLine($"error: {file} deserialized to null (empty or non-object JSON)");
                        badFiles.Add(file);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"error: failed to read {file}: {ex.GetType().Name}: {ex.Message}");
                    badFiles.Add(file);
                }
            }

            if (badFiles.Count > 0)
            {
                Console.Error.WriteLine($"{badFiles.Count} file(s) could not be loaded; aborting merge.");
                return 1;
            }

            if (reports.Count == 0)
            {
                Console.Error.WriteLine($"No reports found in {resultsDir}");
                return 1;
            }

            var (run, index) = MatrixBuilder.MergeReports(reports, commit, commitMessage, branch, repoUrl);

            var runsDir = Path.Combine(outDir, "runs");
            Directory.CreateDirectory(runsDir);

            var runPath = Path.Combine(runsDir, $"{run.Meta.Id}.json");
            await File.WriteAllTextAsync(runPath, JsonSerializer.Serialize(run, WriteOptions), cancellationToken);

            var indexPath = Path.Combine(runsDir, "index.json");
            var entries = await LoadIndexAsync(indexPath, cancellationToken);

            var existingPos = entries.FindIndex(e => e.Id == index.Id);
            if (existingPos >= 0)
                entries[existingPos] = index;
            else
                entries.Add(index);

            await File.WriteAllTextAsync(indexPath, JsonSerializer.Serialize(entries, WriteOptions), cancellationToken);

            Console.WriteLine($"Merged {reports.Count} report(s) -> {runPath}");
            Console.WriteLine($"Index updated -> {indexPath} ({entries.Count} run(s))");
            return 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Merge operation failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static async Task<List<IndexEntry>> LoadIndexAsync(string indexPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(indexPath))
            return [];

        string json;
        try
        {
            json = await File.ReadAllTextAsync(indexPath, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to read index file {indexPath}: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<IndexEntry>>(json)
                ?? throw new InvalidOperationException($"index.json at {indexPath} deserialized to null");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"index.json at {indexPath} contains invalid JSON: {ex.Message}", ex);
        }
    }
}
