using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Search.Sql;

/// <summary>
/// The outcome of a <c>TryCompile</c> call. On a result the compiler returned, exactly one of
/// <see cref="Compiled"/> and <see cref="Failure"/> is non-null; test <see cref="Succeeded"/> rather than
/// either member.
/// </summary>
public sealed record SearchCompilationResult(CompiledSearch? Compiled, SearchCompilationFailure? Failure)
{
    /// <summary>True when SQL was emitted.</summary>
    [MemberNotNullWhen(true, nameof(Compiled))]
    public bool Succeeded => Compiled is not null;
}
