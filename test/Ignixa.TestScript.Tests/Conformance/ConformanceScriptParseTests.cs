using System.Text;
using Ignixa.TestScript.Parsing;

namespace Ignixa.TestScript.Tests.Conformance;

public class ConformanceScriptParseTests
{
    private const string SuitesDirectoryName = "testscripts";

    public static IEnumerable<object[]> ConformanceScriptFiles()
    {
        var root = Path.Combine(AppContext.BaseDirectory, SuitesDirectoryName);
        if (!Directory.Exists(root))
            throw new InvalidOperationException(
                $"Conformance suites not found at '{root}'. They are copied to the output " +
                "directory by src/Core/Ignixa.TestScript.Suites/build/Ignixa.TestScript.Suites.targets, " +
                "which this project imports explicitly — check that the <Import> is still present. " +
                "A ProjectReference does not substitute: build/*.targets auto-import applies to " +
                "PackageReference only.");

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
