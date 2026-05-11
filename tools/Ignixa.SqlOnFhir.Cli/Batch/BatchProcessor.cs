using System.Text.RegularExpressions;

namespace Ignixa.SqlOnFhir.Cli.Batch;

internal static class BatchProcessor
{
    public static IEnumerable<string> DiscoverViewDefinitions(string viewsDir, string pattern)
        => EnumerateMatchingFiles(viewsDir, pattern);

    public static IEnumerable<string> FindInputFiles(string inputDir, string resource, string inputPattern)
    {
        var pattern = inputPattern.Replace("{resource}", resource, StringComparison.OrdinalIgnoreCase);
        return EnumerateMatchingFiles(inputDir, pattern);
    }

    public static string GetOutputPath(string outputDir, string viewDefinitionPath, string format)
    {
        var basename = Path.GetFileNameWithoutExtension(viewDefinitionPath);
        return Path.Combine(outputDir, $"{basename}.{format}");
    }

    private static IEnumerable<string> EnumerateMatchingFiles(string rootDir, string pattern)
    {
        var normalizedPattern = pattern.Replace('\\', '/');
        var matcher = CreateMatcher(normalizedPattern);
        return Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories)
            .Where(file => matcher(Path.GetRelativePath(rootDir, file).Replace('\\', '/')))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    }

    private static Func<string, bool> CreateMatcher(string pattern)
    {
        if (!pattern.Contains('/', StringComparison.Ordinal))
            return path => WildcardMatch(Path.GetFileName(path), pattern);

        var regex = new Regex(GlobToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return path => regex.IsMatch(path);
    }

    private static bool WildcardMatch(string value, string pattern)
        => Regex.IsMatch(
            value,
            GlobToRegex(pattern),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string GlobToRegex(string pattern)
    {
        var regex = new System.Text.StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    i++;
                    if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                    {
                        i++;
                        regex.Append("(?:.*/)?");
                    }
                    else
                    {
                        regex.Append(".*");
                    }
                }
                else
                {
                    regex.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                regex.Append("[^/]");
            }
            else
            {
                regex.Append(Regex.Escape(c.ToString()));
            }
        }
        regex.Append('$');
        return regex.ToString();
    }
}
