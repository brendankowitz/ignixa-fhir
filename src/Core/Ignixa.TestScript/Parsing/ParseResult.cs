using System.Diagnostics.CodeAnalysis;

namespace Ignixa.TestScript.Parsing;

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Factory methods for generic result type")]
public sealed record ParseResult<T> where T : class
{
    public T? Value { get; private init; }
    public IReadOnlyList<ParseError> Errors { get; private init; } = [];
    public bool IsSuccess => Value is not null && !Errors.Any(e => e.Severity == ParseSeverity.Error);
    public bool HasWarnings => Errors.Any(e => e.Severity == ParseSeverity.Warning);

    public static ParseResult<T> Success(T value) => new() { Value = value };

    public static ParseResult<T> Failure(params ParseError[] errors)
    {
        if (errors.Length == 0)
            throw new ArgumentException("At least one error is required.", nameof(errors));
        return new() { Errors = errors };
    }

    public static ParseResult<T> WithWarnings(T value, IReadOnlyList<ParseError> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        if (warnings.Any(e => e.Severity == ParseSeverity.Error))
            throw new ArgumentException(
                "WithWarnings cannot carry Error-severity entries; use Failure instead.", nameof(warnings));
        return new() { Value = value, Errors = warnings };
    }
}
