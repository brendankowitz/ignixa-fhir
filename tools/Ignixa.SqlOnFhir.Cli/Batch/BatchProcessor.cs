namespace Ignixa.SqlOnFhir.Cli.Batch;

internal static class BatchProcessor
{
    public static IEnumerable<string> DiscoverViewDefinitions(string viewsDir, string pattern)
    {
        var filePattern = StripLeadingGlobPrefix(pattern);
        return Directory.EnumerateFiles(viewsDir, filePattern, SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> FindInputFiles(string inputDir, string resource, string inputPattern)
    {
        var filePattern = inputPattern.Replace("{resource}", resource, StringComparison.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(inputDir, filePattern, SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    }

    public static string GetOutputPath(string outputDir, string viewDefinitionPath, string format)
    {
        var basename = Path.GetFileNameWithoutExtension(viewDefinitionPath);
        return Path.Combine(outputDir, $"{basename}.{format}");
    }

    private static string StripLeadingGlobPrefix(string pattern)
    {
        if (pattern.StartsWith("**/", StringComparison.Ordinal) ||
            pattern.StartsWith("**\\", StringComparison.Ordinal))
            return pattern[3..];
        return pattern;
    }
}
