using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Search.Sql;

/// <summary>
/// The outcome of a <c>TryCompile</c> call: exactly one of <see cref="Compiled"/> and
/// <see cref="Failure"/> is non-null. Test <see cref="Succeeded"/> rather than either member.
/// </summary>
/// <remarks>
/// Constructed only through <see cref="Success"/> and <see cref="Failed"/>. The positional-record form
/// would publish a constructor that accepts both members, or neither, and a <c>with</c> expression would
/// reach the same contradictory states through the compiler-generated copy constructor -- which is what
/// makes the <see cref="MemberNotNullWhenAttribute"/> pair below sound rather than aspirational.
/// </remarks>
public sealed record SearchCompilationResult
{
    private SearchCompilationResult(CompiledSearch? compiled, SearchCompilationFailure? failure)
    {
        Compiled = compiled;
        Failure = failure;
    }

    /// <summary>The emitted SQL, when emission succeeded.</summary>
    public CompiledSearch? Compiled { get; }

    /// <summary>The failure, when it did not.</summary>
    public SearchCompilationFailure? Failure { get; }

    /// <summary>True when SQL was emitted.</summary>
    [MemberNotNullWhen(true, nameof(Compiled))]
    [MemberNotNullWhen(false, nameof(Failure))]
    public bool Succeeded => Compiled is not null;

    /// <summary>A successful outcome carrying <paramref name="compiled"/>.</summary>
    public static SearchCompilationResult Success(CompiledSearch compiled)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        return new SearchCompilationResult(compiled, failure: null);
    }

    /// <summary>A failed outcome carrying <paramref name="failure"/>.</summary>
    public static SearchCompilationResult Failed(SearchCompilationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new SearchCompilationResult(compiled: null, failure);
    }
}
