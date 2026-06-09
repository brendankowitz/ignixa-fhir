using Ignixa.TestScript.Expressions;

namespace Ignixa.TestScript.Model;

public sealed record TestPhaseDefinition
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<ActionExpression> Actions { get; init; } = [];
    public ParametrizeDefinition? Parameters { get; init; }
    public IReadOnlyList<string> FhirVersions { get; init; } = [];
}
