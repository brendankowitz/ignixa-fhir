using Ignixa.TestScript.Expressions;

namespace Ignixa.TestScript.Model;

public sealed record TestScriptDefinition
{
    public required TestScriptMetadata Metadata { get; init; }
    public IReadOnlyList<ProfileReference> Profiles { get; init; } = [];
    public IReadOnlyList<FixtureDefinition> Fixtures { get; init; } = [];
    public IReadOnlyList<VariableDefinition> Variables { get; init; } = [];
    public IReadOnlyList<ActionExpression> Setup { get; init; } = [];
    public IReadOnlyList<TestPhaseDefinition> Tests { get; init; } = [];
    public IReadOnlyList<OperationExpression> Teardown { get; init; } = [];
}
