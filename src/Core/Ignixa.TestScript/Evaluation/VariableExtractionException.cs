namespace Ignixa.TestScript.Evaluation;

public sealed class VariableExtractionException : Exception
{
    public VariableExtractionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
