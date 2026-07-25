namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>One corpus entry carried through compilation, canonicalization, and comparison.</summary>
public sealed record DifferentialResult(
    CorpusEntry Entry,
    CorpusCompilation Compilation,
    SqlShape Legacy,
    SqlShape? Compiled,
    ShapeComparison? Comparison)
{
    public ShapeVerdict Verdict => Comparison?.Verdict ?? ShapeVerdict.NotCompiled;
}
