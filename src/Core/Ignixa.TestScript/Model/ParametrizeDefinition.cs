namespace Ignixa.TestScript.Model;

public sealed record ParametrizeDefinition(string VariableName, IReadOnlyList<string> Values);
