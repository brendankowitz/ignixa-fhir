using System.Reflection;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Xunit.Sdk;

namespace Ignixa.TestScript.XUnit;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class TestScriptDataAttribute : DataAttribute
{
    public string GlobPattern { get; }
    public string? BasePath { get; }

    public TestScriptDataAttribute(string globPattern, string? basePath = null)
    {
        GlobPattern = globPattern;
        BasePath = basePath;
    }

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        var baseDirectory = BasePath ?? AppContext.BaseDirectory;
        var matcher = new Matcher();
        matcher.AddInclude(GlobPattern);

        var directoryInfo = new DirectoryInfoWrapper(new DirectoryInfo(baseDirectory));
        var matchResult = matcher.Execute(directoryInfo);

        if (!matchResult.HasMatches)
            throw new InvalidOperationException(
                $"No test script files matched glob pattern '{GlobPattern}' in '{baseDirectory}'.");

        foreach (var file in matchResult.Files)
        {
            var fullPath = Path.Combine(baseDirectory, file.Path);
            yield return [fullPath];
        }
    }
}
