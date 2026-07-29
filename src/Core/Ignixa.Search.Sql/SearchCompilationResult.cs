using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Search.Sql;

/// <summary>The outcome of a <c>TryCompile</c> call: exactly one of <see cref="Compiled"/> or <see cref="Failure"/> is non-null.</summary>
public sealed record SearchCompilationResult(CompiledSearch? Compiled, SearchCompilationFailure? Failure)
{
    /// <summary>True when SQL was emitted.</summary>
    [MemberNotNullWhen(true, nameof(Compiled))]
    public bool Succeeded => Compiled is not null;
}
