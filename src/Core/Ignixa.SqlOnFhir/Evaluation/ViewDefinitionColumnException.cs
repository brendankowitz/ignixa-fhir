namespace Ignixa.SqlOnFhir.Evaluation;

/// <summary>
/// A ViewDefinition column cannot represent what its path selected. The message names the column, its declared type and
/// the selected element's FHIR type and location, never resource values, so it is safe to show or persist.
/// </summary>
public sealed class ViewDefinitionColumnException : InvalidOperationException
{
    public ViewDefinitionColumnException(string message)
        : base(message)
    {
    }
}
