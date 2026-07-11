namespace Ignixa.TestScript.Parsing;

/// <summary>
/// Thrown by <see cref="TestScriptContentNormalizer"/> when a recognized Ignixa shorthand
/// property is malformed, or when a shorthand and its canonical extension form are both
/// present but disagree. Callers that want normalization errors folded into a
/// <see cref="ParseResult{T}"/> (rather than propagated as an exception) should use
/// <see cref="TestScriptParser.Parse(string)"/>, which catches this exception and converts it
/// into a <see cref="ParseSeverity.Error"/> entry.
/// </summary>
public sealed class TestScriptNormalizationException : Exception
{
    public TestScriptNormalizationException(string message)
        : base(message)
    {
    }
}
