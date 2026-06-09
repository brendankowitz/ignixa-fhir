namespace Ignixa.TestScript.Model;

public sealed record ParametrizeDefinition
{
    public string VariableName { get; }
    public IReadOnlyList<string> Values { get; }

    public ParametrizeDefinition(string variableName, IReadOnlyList<string> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableName);
        if (values.Count == 0)
            throw new ArgumentException("Parametrize must have at least one value.", nameof(values));
        VariableName = variableName;
        Values = values;
    }
}
