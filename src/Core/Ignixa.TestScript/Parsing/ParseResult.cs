using System.Diagnostics.CodeAnalysis;

namespace Ignixa.TestScript.Parsing;

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Factory methods for generic result type")]
public sealed record ParseResult<T>
{
    public T? Value { get; init; }
    public IReadOnlyList<ParseError> Errors { get; init; } = [];
    public bool IsSuccess => Value is not null && !Errors.Any(e => e.Severity == ParseSeverity.Error);

    public static ParseResult<T> Success(T value) => new() { Value = value };

    public static ParseResult<T> Failure(params ParseError[] errors) =>
        new() { Errors = errors };

    public static ParseResult<T> WithWarnings(T value, IReadOnlyList<ParseError> warnings) =>
        new() { Value = value, Errors = warnings };
}
