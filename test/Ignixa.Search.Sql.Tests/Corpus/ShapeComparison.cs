namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>
/// A multiset comparison of two shapes. Tables and filters decide the verdict; operations are reported
/// but never decide it, because the two dialects legitimately express the same set algebra with
/// different operators (correlated EXISTS versus INNER JOIN, NOT IN versus NOT EXISTS).
/// </summary>
public sealed record ShapeComparison(
    ShapeVerdict Verdict,
    IReadOnlyList<string> OnlyInLegacy,
    IReadOnlyList<string> OnlyInCompiler,
    IReadOnlyList<string> OperationDifferences)
{
    public static ShapeComparison Compare(SqlShape legacy, SqlShape compiler)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(compiler);

        var onlyInLegacy = new List<string>();
        var onlyInCompiler = new List<string>();

        Subtract("table", legacy.Tables, compiler.Tables, onlyInLegacy, onlyInCompiler);
        Subtract("filter", legacy.Filters, compiler.Filters, onlyInLegacy, onlyInCompiler);

        var operations = new List<string>();
        var left = new List<string>();
        var right = new List<string>();
        Subtract("op", legacy.Operations, compiler.Operations, left, right);
        operations.AddRange(left.Select(o => "legacy: " + o));
        operations.AddRange(right.Select(o => "compiler: " + o));

        return new ShapeComparison(Decide(onlyInLegacy, onlyInCompiler), onlyInLegacy, onlyInCompiler, operations);
    }

    private static ShapeVerdict Decide(List<string> onlyInLegacy, List<string> onlyInCompiler) =>
        (onlyInLegacy.Count, onlyInCompiler.Count) switch
        {
            (0, 0) => ShapeVerdict.Match,
            (> 0, 0) => ShapeVerdict.CompilerDoesLess,
            (0, > 0) => ShapeVerdict.CompilerDoesMore,
            _ => ShapeVerdict.Divergent,
        };

    private static void Subtract(
        string kind,
        IReadOnlyDictionary<string, int> legacy,
        IReadOnlyDictionary<string, int> compiler,
        List<string> onlyInLegacy,
        List<string> onlyInCompiler)
    {
        foreach (var (key, count) in legacy.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var surplus = count - (compiler.TryGetValue(key, out var mirrored) ? mirrored : 0);
            if (surplus > 0)
            {
                onlyInLegacy.Add(Format(kind, key, surplus));
            }
        }

        foreach (var (key, count) in compiler.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var surplus = count - (legacy.TryGetValue(key, out var mirrored) ? mirrored : 0);
            if (surplus > 0)
            {
                onlyInCompiler.Add(Format(kind, key, surplus));
            }
        }
    }

    private static string Format(string kind, string key, int surplus)
        => surplus == 1 ? $"{kind} {key}" : $"{kind} {key} (x{surplus})";
}
