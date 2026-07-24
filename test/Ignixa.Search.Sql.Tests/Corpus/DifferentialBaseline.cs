namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>
/// What the corpus currently achieves. Guarded so a change that narrows the compiler's real-world
/// coverage fails the build; the differential report explains what each remaining gap is.
/// </summary>
public static class DifferentialBaseline
{
    /// <summary>Captured queries the compiler can turn into SQL today.</summary>
    public const int CompiledQueries = 185;
}
