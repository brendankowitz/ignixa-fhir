using System.Text;
using Ignixa.TestScript.Parsing;

namespace Ignixa.TestScript.Tests.Conformance;

public class ConformanceScriptParseTests
{
    private static string? LocateConformanceTestsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "conformance-tests");
            if (Directory.Exists(candidate))
                return candidate;

            if (File.Exists(Path.Combine(dir.FullName, "All.sln")))
            {
                candidate = Path.Combine(dir.FullName, "conformance-tests");
                if (Directory.Exists(candidate))
                    return candidate;
                break;
            }

            dir = dir.Parent;
        }
        return null;
    }

    public static IEnumerable<object[]> ConformanceScriptFiles()
    {
        var root = LocateConformanceTestsRoot();
        if (root is null)
            throw new InvalidOperationException(
                "conformance-tests directory not found. Expected it to be a sibling of All.sln or an ancestor of the test output directory. " +
                "Ensure the conformance-tests directory exists at the repository root.");

        return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Select(path => new object[] { path });
    }

    [Theory]
    [MemberData(nameof(ConformanceScriptFiles))]
    public void GivenConformanceScript_WhenParsing_ThenSucceedsWithNoErrorsOrWarnings(string filePath)
    {
        var result = TestScriptParser.ParseFile(filePath);

        if (!result.IsSuccess || result.HasWarnings)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Parse failed for: {filePath}");
            foreach (var error in result.Errors)
                sb.AppendLine($"  [{error.Severity}] {error.Path ?? "<root>"}: {error.Message}");

            result.IsSuccess.ShouldBeTrue(sb.ToString());
            result.HasWarnings.ShouldBeFalse(sb.ToString());
        }
    }
}
