namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// A reindex job parameter outside its accepted range.
/// </summary>
public sealed class ReindexValidationException(string parameterName, int minimum, int maximum)
    : Exception($"{parameterName} must be between {minimum} and {maximum}.")
{
    public string ParameterName { get; } = parameterName;

    public int Minimum { get; } = minimum;

    public int Maximum { get; } = maximum;
}
